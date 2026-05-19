import os

class Config:
    provider_id    = os.environ.get("PROVIDER_ID",      "forecast-python")
    client_id      = os.environ.get("CLIENT_ID",        "")
    client_secret  = os.environ.get("CLIENT_SECRET",    "")
    token_endpoint = os.environ.get("TOKEN_ENDPOINT",   "http://request-api:8080/api/v1/providers/token")
    bridge_endpoint = os.environ.get("BRIDGE_ENDPOINT", "provider-bridge:5400")
    admin_base     = os.environ.get("ADMIN_BASE",       "http://request-api:8080/api/v1/admin")
    version        = os.environ.get("PROVIDER_VERSION", "1.0.0")
    selfcheck      = "--selfcheck" in __import__("sys").argv
