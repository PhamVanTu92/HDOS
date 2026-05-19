#!/usr/bin/env bash
set -e
python -m grpc_tools.protoc \
  -I ../../proto \
  --python_out=. \
  --grpc_python_out=. \
  ../../proto/provider.proto
echo "Proto files generated: provider_pb2.py, provider_pb2_grpc.py"
