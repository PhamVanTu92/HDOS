FROM python:3.11-slim

WORKDIR /app
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy proto and generate stubs
COPY ../../proto/provider.proto /proto/provider.proto
RUN python -m grpc_tools.protoc -I /proto --python_out=/app --grpc_python_out=/app /proto/provider.proto

COPY . .

ENV PYTHONUNBUFFERED=1
ENTRYPOINT ["python", "main.py"]
