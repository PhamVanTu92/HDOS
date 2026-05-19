import logging

logger = logging.getLogger(__name__)

class HandlerRegistry:
    def __init__(self):
        self._handlers = {}

    def register(self, operation: str, handler_fn):
        """Register a callable: handler_fn(request, send_progress) -> (payload_dict, status)"""
        self._handlers[operation] = handler_fn

    def resolve(self, operation: str):
        return self._handlers.get(operation)

    @property
    def operations(self):
        return list(self._handlers.keys())
