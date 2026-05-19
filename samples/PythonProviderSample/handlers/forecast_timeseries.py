"""forecast.timeseries — mock sinusoidal prediction with progress reporting."""
import math, time, json, logging

logger = logging.getLogger(__name__)

def handle(request, send_progress):
    """
    request: OperationRequest proto message
    send_progress: callable(percent: int, message: str)
    Returns: (payload_dict, "DONE" | "FAILED" | "CANCELLED")
    """
    try:
        params = json.loads(request.params_json)
    except Exception as e:
        return ({"error": str(e)}, "FAILED")

    series_id   = params.get("seriesId", "unknown")
    horizon     = int(params.get("horizon", 12))
    granularity = params.get("granularity", "monthly")

    predictions = []
    progress_steps = 5

    for step in range(progress_steps):
        time.sleep(horizon * 0.002)  # mock latency proportional to horizon
        chunk_end = int((step + 1) / progress_steps * horizon)
        chunk_start = int(step / progress_steps * horizon)

        for i in range(chunk_start, chunk_end):
            period = i + 1
            value  = 100.0 + 20.0 * math.sin(2 * math.pi * period / 12)
            lower  = value * 0.85
            upper  = value * 1.15
            predictions.append({
                "period": period,
                "value":  round(value, 2),
                "lower":  round(lower, 2),
                "upper":  round(upper, 2),
            })

        percent = min(99, int((step + 1) / progress_steps * 99))
        if request.wants_progress:
            send_progress(percent, f"Generated {chunk_end}/{horizon} periods")

    payload = {
        "seriesId":     series_id,
        "predictions":  predictions[:horizon],
        "modelVersion": "mock-v1",
        "confidence":   0.87,
    }
    return (payload, "DONE")
