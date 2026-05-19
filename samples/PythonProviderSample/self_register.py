"""Self-registration on startup (dev mode only)."""
import json, logging, sys
import requests

logger = logging.getLogger(__name__)

OPERATIONS = [
    {
        "operation":    "forecast.timeseries",
        "timeoutMs":    30000,
        "paramsSchema": {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "type": "object",
            "required": ["seriesId", "horizon"],
            "properties": {
                "seriesId":    {"type": "string", "minLength": 1, "maxLength": 128},
                "horizon":     {"type": "integer", "minimum": 1, "maximum": 120},
                "granularity": {"type": "string", "enum": ["daily", "weekly", "monthly"], "default": "monthly"}
            },
            "additionalProperties": False
        },
        "payloadSchema": {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "type": "object",
            "required": ["seriesId", "predictions", "modelVersion", "confidence"],
            "properties": {
                "seriesId":    {"type": "string"},
                "predictions": {"type": "array", "items": {
                    "type": "object", "required": ["period", "value", "lower", "upper"],
                    "properties": {
                        "period": {"type": "integer", "minimum": 1},
                        "value":  {"type": "number"},
                        "lower":  {"type": "number"},
                        "upper":  {"type": "number"}
                    }
                }},
                "modelVersion": {"type": "string"},
                "confidence":   {"type": "number", "minimum": 0.0, "maximum": 1.0}
            },
            "additionalProperties": False
        }
    }
]

def self_register(config):
    admin_base = getattr(config, 'admin_base', 'http://request-api:8080/api/v1/admin')
    provider_id = config.provider_id

    # Check if already registered
    try:
        r = requests.get(f"{admin_base}/providers/{provider_id}", timeout=5)
        if r.status_code == 200:
            logger.info("Provider %s already registered", provider_id)
            return
        if r.status_code != 404:
            logger.warning("Unexpected status %d checking registration — skipping", r.status_code)
            return
    except Exception as e:
        logger.warning("Cannot reach admin API: %s — skipping self-registration", e)
        return

    # Register provider
    reg_payload = {
        "providerId":          provider_id,
        "displayName":         "Python Forecast Provider (Sample)",
        "description":         "Mock time-series forecast using sinusoidal model",
        "operations":          ["forecast.timeseries"],
        "timeoutMs":           30000,
        "maxConcurrentRequests": 4,
    }
    try:
        r = requests.post(f"{admin_base}/providers", json=reg_payload, timeout=10)
        if not r.ok:
            logger.error("Registration failed: %d %s", r.status_code, r.text)
            sys.exit(1)
        data = r.json()
        logger.warning("Registered! SAVE clientSecret NOW (shown once): clientId=%s secret=%s",
                       data.get("clientId"), data.get("clientSecret"))

        # Register operations
        for op in OPERATIONS:
            r2 = requests.post(f"{admin_base}/operations", json={**op, "providerId": provider_id}, timeout=10)
            if not r2.ok:
                logger.warning("Operation registration failed: %s", r2.text)
    except Exception as e:
        logger.error("Self-registration error: %s", e)
        sys.exit(1)
