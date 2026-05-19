#!/usr/bin/env python3
"""
PythonProviderSample — forecast.timeseries provider.

Usage:
  python main.py              # normal mode (requires live platform)
  python main.py --selfcheck  # dry-run: verify imports + config, exit 0

Environment variables:
  PROVIDER_ID, CLIENT_ID, CLIENT_SECRET, TOKEN_ENDPOINT, BRIDGE_ENDPOINT, ADMIN_BASE
"""

import logging, sys
logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger(__name__)

def main():
    from config import Config
    config = Config()

    if config.selfcheck:
        # Dry-run: verify imports and config shape, exit 0
        logger.info("Self-check mode: verifying imports...")
        try:
            import grpc  # noqa
            logger.info("  grpc: OK (%s)", grpc.__version__)
        except ImportError as e:
            logger.error("  grpc import FAILED: %s", e)
            sys.exit(1)

        try:
            import provider_pb2  # noqa
            import provider_pb2_grpc  # noqa
            logger.info("  proto stubs: OK")
        except ImportError:
            logger.warning("  proto stubs not generated yet — run generate_proto.sh first (expected in CI)")

        logger.info("  Config.provider_id: %s", config.provider_id)
        logger.info("  Config.token_endpoint: %s", config.token_endpoint)
        logger.info("  Config.bridge_endpoint: %s", config.bridge_endpoint)
        logger.info("Self-check passed.")
        return

    # Normal mode
    from token_manager import TokenManager
    from handler_registry import HandlerRegistry
    from handlers.forecast_timeseries import handle as forecast_handle
    from provider_client import ProviderClient
    from self_register import self_register

    self_register(config)

    registry = HandlerRegistry()
    registry.register("forecast.timeseries", forecast_handle)

    tokens = TokenManager(config)

    def on_revoked():
        logger.critical("Credentials revoked — stopping process")
        sys.exit(2)

    client = ProviderClient(config, tokens, registry, on_revoked=on_revoked)
    logger.info("Starting provider client (provider_id=%s)", config.provider_id)
    client.run()
    logger.info("Provider client stopped")

if __name__ == "__main__":
    main()
