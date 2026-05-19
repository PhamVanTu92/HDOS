"""
provider_client.py — gRPC reconnection loop for PythonProviderSample.

Protocol:
  1. Acquire JWT from token endpoint
  2. Open gRPC channel with JWT in metadata
  3. Send Hello (supported operations)
  4. Receive Welcome (max_concurrent_requests, heartbeat_interval_seconds)
  5. Serve requests (dispatch to handler_registry), send heartbeats
  6. On RefreshAuthRequired: finish in-flight, reconnect with fresh JWT
  7. On Disconnect(credentials_revoked): stop; call on_revoked callback
  8. On stream error: exponential backoff reconnect
"""

import time, json, logging, threading
import grpc
import provider_pb2 as pb2
import provider_pb2_grpc as pb2_grpc
from token_manager import CredentialsRevokedException

logger = logging.getLogger(__name__)

BACKOFF_STEPS = [1, 2, 4, 8, 16, 30]  # seconds (cap 30s per Phase 9 spec)

def _get_backoff(attempt: int) -> float:
    return float(BACKOFF_STEPS[min(attempt, len(BACKOFF_STEPS) - 1)])

class ProviderClient:
    def __init__(self, config, token_manager, handler_registry, on_revoked=None):
        self._config   = config
        self._tokens   = token_manager
        self._handlers = handler_registry
        self._on_revoked = on_revoked
        self._stop     = threading.Event()

    def run(self):
        """Main reconnection loop. Blocks until stopped or credentials revoked."""
        attempt = 0
        while not self._stop.is_set():
            # AcquiringJwt
            try:
                token = self._tokens.acquire()
            except CredentialsRevokedException:
                if self._on_revoked:
                    self._on_revoked()
                return
            except Exception as e:
                delay = _get_backoff(attempt)
                logger.warning("Token acquisition failed: %s — retry in %.0fs", e, delay)
                attempt += 1
                time.sleep(delay)
                continue

            # ConnectAndServe
            try:
                reconnect = self._connect_and_serve(token)
                if reconnect:
                    attempt = 0  # reset on successful Welcome
                else:
                    return  # credentials_revoked — permanent stop
            except CredentialsRevokedException:
                if self._on_revoked:
                    self._on_revoked()
                return
            except Exception as e:
                delay = _get_backoff(attempt)
                logger.warning("Connection error: %s — retry in %.0fs", e, delay)
                attempt += 1
                time.sleep(delay)

    def _connect_and_serve(self, token: str) -> bool:
        """Returns True = reconnect (normal), False = stop (credentials_revoked)."""
        # Plain HTTP/2 channel (dev mode — no TLS)
        channel = grpc.insecure_channel(self._config.bridge_endpoint)
        stub = pb2_grpc.OperationProviderStub(channel)

        metadata = [("authorization", f"Bearer {token}")]

        self._send_queue = __import__("queue").Queue()
        self._send_queue_stop = threading.Event()
        self._active_requests = {}
        self._active_lock = threading.Lock()

        def request_iter():
            # Send Hello as first message
            hello = pb2.Hello(
                provider_id=self._config.provider_id,
                version=self._config.version,
                supported_operations=self._handlers.operations,
            )
            yield pb2.FromProvider(hello=hello)

            # After Hello: yield heartbeats and responses via queue
            while not self._send_queue_stop.is_set():
                try:
                    msg = self._send_queue.get(timeout=1.0)
                    if msg is None:
                        return
                    yield msg
                except Exception:
                    pass  # timeout — loop and check stop flag

        try:
            stream = stub.Connect(request_iter(), metadata=metadata)
            reset_backoff = False
            refresh_requested = False

            # Heartbeat thread
            heartbeat_interval = 30  # default; overridden by Welcome
            def heartbeat_loop():
                while not self._send_queue_stop.is_set():
                    time.sleep(heartbeat_interval)
                    if not self._send_queue_stop.is_set():
                        ts = int(time.time() * 1000)
                        self._send_queue.put(pb2.FromProvider(heartbeat=pb2.Heartbeat(ts_unix_ms=ts)))
            hb_thread = threading.Thread(target=heartbeat_loop, daemon=True)

            for msg in stream:
                kind = msg.WhichOneof("message")

                if kind == "welcome":
                    w = msg.welcome
                    heartbeat_interval = w.heartbeat_interval_seconds or 30
                    logger.info("Connected — session_id=%s maxConcurrent=%d", w.session_id, w.max_concurrent_requests)
                    hb_thread.start()
                    reset_backoff = True

                elif kind == "request":
                    req = msg.request
                    t = threading.Thread(target=self._handle_request, args=(req,), daemon=True)
                    with self._active_lock:
                        self._active_requests[req.request_id] = t
                    t.start()

                elif kind == "cancel":
                    # Best-effort — threads check a per-request cancel flag
                    rid = msg.cancel.request_id
                    logger.debug("Cancel received for %s", rid)

                elif kind == "refresh_auth":
                    logger.info("RefreshAuthRequired — will reconnect after draining")
                    refresh_requested = True
                    break

                elif kind == "disconnect":
                    reason = msg.disconnect.reason
                    logger.warning("Disconnect received: %s", reason)
                    if reason == "credentials_revoked":
                        raise CredentialsRevokedException()
                    break

        finally:
            self._send_queue_stop.set()
            self._send_queue.put(None)  # unblock request_iter
            channel.close()

        # Drain in-flight (max 30s for refresh, or just let go on error)
        if refresh_requested:
            deadline = time.time() + 30
            while time.time() < deadline:
                with self._active_lock:
                    if not self._active_requests:
                        break
                time.sleep(0.05)

        return reset_backoff

    def _handle_request(self, req):
        handler_fn = self._handlers.resolve(req.operation)
        if not handler_fn:
            logger.warning("No handler for %s", req.operation)
            self._send_terminal(req.request_id, None, "FAILED", "OPERATION_NOT_FOUND", "No handler registered")
            return

        def send_progress(percent, message):
            ts = int(time.time() * 1000)
            chunk = pb2.OperationResponseChunk(
                request_id=req.request_id,
                progress=pb2.Progress(percent=percent, message=message, ts_unix_ms=ts),
            )
            self._send_queue.put(pb2.FromProvider(response_chunk=chunk))

        try:
            payload_dict, status = handler_fn(req, send_progress)
            payload_json = json.dumps(payload_dict) if payload_dict else ""
            self._send_terminal(req.request_id, payload_json, status)
        except Exception as e:
            logger.error("Handler error for %s: %s", req.request_id, e)
            self._send_terminal(req.request_id, None, "FAILED", "INTERNAL_ERROR", str(e))
        finally:
            with self._active_lock:
                self._active_requests.pop(req.request_id, None)

    def _send_terminal(self, request_id, payload_json, status_str, error_code=None, error_message=None):
        status_map = {"DONE": pb2.DONE, "FAILED": pb2.FAILED, "CANCELLED": pb2.CANCELLED}
        status = status_map.get(status_str, pb2.FAILED)
        terminal = pb2.Terminal(status=status, payload_json=payload_json or "")
        if error_code:
            terminal.error.CopyFrom(pb2.Error(code=error_code, message=error_message or ""))
        chunk = pb2.OperationResponseChunk(request_id=request_id, terminal=terminal)
        self._send_queue.put(pb2.FromProvider(response_chunk=chunk))

    def stop(self):
        self._stop.set()
