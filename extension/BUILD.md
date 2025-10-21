# RepoRunner Extension - Build Instructions

## Quick Build

```bash
npm install
npm run build
```

**Note:** The first `npm install` may take a minute to download dependencies including TypeScript types for Chrome APIs. This is normal and only happens once.

Output will be in `dist/` folder.

## Load in Browser

1. Open Chrome/Edge
2. Navigate to `chrome://extensions` or `edge://extensions`
3. Enable "Developer mode" (toggle in top-right)
4. Click "Load unpacked"
5. Select the `dist` folder
6. Done! The extension is now active

## Verify Installation

1. Go to `https://github.com/docker/getting-started`
2. You should see a purple "▶ Run Locally" button
3. Click it to test the popup

## Development Mode

```bash
npm run dev
```

This watches for file changes and rebuilds automatically.

## Requirements

- Node.js 20+
- npm 10+
- Gateway running on `http://localhost:5000`

## Troubleshooting

### TypeScript errors in VS Code

If you see TypeScript errors like `Cannot find name 'chrome'` in VS Code:

1. **First time setup:** Run `npm install` to install `@types/chrome`
2. **Restart VS Code:** TypeScript language server needs to reload
3. **Check tsconfig.json:** Ensure it includes `"types": ["chrome"]`

These errors don't affect the build - Webpack will compile successfully even if VS Code shows red squiggles.

### `npm install` fails
- Try: `npm install --legacy-peer-deps`
- Or: `npm cache clean --force && npm install`

### Button doesn't appear on GitHub
- Check browser console for errors (F12)
- Refresh the page
- Ensure the repo has a Dockerfile or docker-compose.yml

### TypeScript errors
- Run: `npm install --save-dev @types/chrome`
- These are expected during development and don't affect the build

## Files Generated

After build, `dist/` contains:
- `manifest.json` - Extension manifest
- `background.js` - Service worker
- `content.js` - GitHub page content script  
- `popup.js` - Popup UI logic
- `popup.html` - Popup HTML
- `popup.css` - Popup styles
- Icon files (placeholder)

## Next Steps

See `docs/Milestone-4-QuickStart.md` for full testing instructions.
