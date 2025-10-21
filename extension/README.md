# RepoRunner Browser Extension

Chrome/Edge extension for running Dockerized GitHub repos locally with one click.

## Features

- 🔍 Auto-detects Dockerfile or docker-compose.yml in GitHub repos
- ▶️ One-click "Run Locally" button injected into GitHub UI
- 🚀 Automatically determines mode (DOCKERFILE vs COMPOSE)
- 🎯 For docker-compose repos: intelligently infers primary service
- 📊 Real-time status updates (Queued → Building → Running → Succeeded)
- ⏹️ Stop running containers with one click

## Setup

### Prerequisites

- Node.js 20+ and npm
- Chromium-based browser (Chrome, Edge, Opera GX, Brave, etc.)
- RepoRunner Gateway running (default: http://localhost:5000)

### Build Extension

```bash
cd extension
npm install
npm run build
```

The built extension will be in `extension/dist/`.

### Load Unpacked Extension

**Chrome/Edge:**
1. Open Chrome/Edge
2. Navigate to `chrome://extensions` (or `edge://extensions`)
3. Enable "Developer mode"
4. Click "Load unpacked"
5. Select the `extension/dist` folder

**Opera GX:**
1. Open Opera GX
2. Navigate to `opera://extensions`
3. Enable "Developer mode"
4. Click "Load unpacked"
5. Select the `extension/dist` folder

**Note:** The extension works on all Chromium-based browsers (Chrome, Edge, Opera GX, Brave, Vivaldi, etc.)

## Usage

1. Navigate to any GitHub repository with a Dockerfile or docker-compose.yml
2. Look for the green "▶ Run Locally" button near the Code/Issues tabs
3. Click the button to open the RepoRunner popup
4. For docker-compose repos: select the primary service if needed
5. Click "Start Run" and watch the status transitions
6. Once running, click the preview URL to see your app
7. Click "Stop" when done

## Development

```bash
# Watch mode (rebuilds on file changes)
npm run dev

# Lint
npm run lint
```

## Configuration

Edit `src/popup.ts` to change the Gateway URL:

```typescript
const GATEWAY_URL = 'http://localhost:5000';
```

## Architecture

- **content.ts**: Scans GitHub DOM for Dockerfile/compose files, injects button
- **popup.ts**: UI logic for starting/stopping runs and polling status
- **background.ts**: Service worker for extension lifecycle

## TODO

- [ ] Generate actual gRPC-Web TypeScript stubs from proto files
- [ ] Add icons (currently placeholder)
- [ ] Add settings page for configuring Gateway URL
- [ ] Stream logs in popup
- [ ] Add "Ask the Repo" RAG interface
