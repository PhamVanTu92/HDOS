import time
import requests
import logging

logger = logging.getLogger(__name__)

class CredentialsRevokedException(Exception):
    pass

class TokenManager:
    def __init__(self, config):
        self._config = config
        self._token = None
        self._issued_at = 0.0
        self._expires_in = 0
        self._consecutive_401s = 0

    def get_token(self):
        """Returns cached token if >90s remaining; else fetches fresh."""
        if self._token and time.time() < self._issued_at + self._expires_in - 90:
            return self._token
        return self._fetch()

    def acquire(self):
        """Always fetches a fresh token."""
        return self._fetch()

    def should_refresh(self):
        """True when past 80% of token lifetime."""
        if not self._token:
            return False
        return time.time() > self._issued_at + self._expires_in * 0.80

    def _fetch(self):
        try:
            resp = requests.post(
                self._config.token_endpoint,
                json={
                    "clientId":     self._config.client_id,
                    "clientSecret": self._config.client_secret,
                    "grantType":    "client_credentials",
                },
                timeout=10,
            )
        except Exception as e:
            logger.warning("Token endpoint network error: %s", e)
            raise

        if resp.status_code == 400:
            raise RuntimeError(f"Token endpoint 400 Bad Request: {resp.text}")

        if resp.status_code == 401:
            self._consecutive_401s += 1
            logger.warning("Token endpoint 401 (consecutive=%d)", self._consecutive_401s)
            if self._consecutive_401s >= 5:
                logger.critical("5 consecutive 401s — credentials revoked")
                raise CredentialsRevokedException()
            raise RuntimeError(f"401 Unauthorized (attempt {self._consecutive_401s})")

        if resp.status_code == 429:
            retry_after = int(resp.headers.get("Retry-After", "60"))
            raise RuntimeError(f"429 Too Many Requests — retry after {retry_after}s")

        resp.raise_for_status()
        self._consecutive_401s = 0
        data = resp.json()
        self._token = data["accessToken"]
        self._issued_at = time.time()
        self._expires_in = data["expiresIn"]
        logger.debug("Acquired JWT — expiresIn=%ds", self._expires_in)
        return self._token
