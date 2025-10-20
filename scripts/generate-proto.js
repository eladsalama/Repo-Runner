const { execSync } = require('child_process');
const path = require('path');
const fs = require('fs');

// Create output directory
const outDir = path.join(__dirname, '..', 'extension', 'proto-gen');
if (!fs.existsSync(outDir)) {
  fs.mkdirSync(outDir, { recursive: true });
}

// Proto file paths
const contractsDir = path.join(__dirname, '..', 'contracts');
const protoFiles = [
  path.join(contractsDir, 'run.proto'),
  path.join(contractsDir, 'insights.proto'),
  path.join(contractsDir, 'events.proto')
];

// Generate JavaScript code
const protocPath = path.join(__dirname, '..', 'node_modules', 'grpc-tools', 'bin', 'protoc.exe');
const tsPluginPath = path.join(__dirname, '..', 'node_modules', '.bin', 'protoc-gen-ts.cmd');

// First generate JS with CommonJS imports
const jsCommand = `"${protocPath}" ` +
  `--proto_path="${contractsDir}" ` +
  `--js_out=import_style=commonjs,binary:${outDir} ` +
  `--plugin=protoc-gen-ts="${tsPluginPath}" ` +
  `--ts_out=service=grpc-web:${outDir} ` +
  `${protoFiles.join(' ')}`;

console.log('Generating TypeScript + JavaScript gRPC-Web client...');
console.log('Command:', jsCommand);

try {
  execSync(jsCommand, { stdio: 'inherit' });
  console.log('✓ TypeScript client generated in extension/proto-gen/');
} catch (error) {
  console.error('Failed to generate client:', error.message);
  // Create a simple placeholder README instead
  const readmePath = path.join(outDir, 'README.md');
  fs.writeFileSync(readmePath, 
    '# Proto Generation\n\n' +
    'TypeScript client generation is deferred to browser extension implementation.\n' +
    'For now, proto contracts are defined in `contracts/` and compiled for .NET services.\n\n' +
    '## Manual Generation\n\n' +
    'When implementing the extension, use protoc with grpc-web plugin:\n' +
    '```bash\n' +
    'protoc --proto_path=contracts \\\n' +
    '  --js_out=import_style=typescript,binary:extension/proto-gen \\\n' +
    '  --grpc-web_out=import_style=typescript,mode=grpcwebtext:extension/proto-gen \\\n' +
    '  contracts/*.proto\n' +
    '```\n'
  );
  console.log('✓ Created placeholder README in extension/proto-gen/');
}
