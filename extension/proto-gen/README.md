# Proto Generation

TypeScript client generation is deferred to browser extension implementation.
For now, proto contracts are defined in `contracts/` and compiled for .NET services.

## Manual Generation

When implementing the extension, use protoc with grpc-web plugin:
```bash
protoc --proto_path=contracts \
  --js_out=import_style=typescript,binary:extension/proto-gen \
  --grpc-web_out=import_style=typescript,mode=grpcwebtext:extension/proto-gen \
  contracts/*.proto
```
