// popup.ts - Extension popup (for LLM chat - to be implemented)

// Render immediately without waiting for DOMContentLoaded
const container = document.getElementById('popup-container');
if (container) {
  container.innerHTML = `
    <div style="padding: 20px; text-align: center; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif;">
      <svg height="48" viewBox="0 0 16 16" version="1.1" width="48" style="margin-bottom: 12px; fill: #1f883d;">
        <path d="M8 0a8 8 0 1 1 0 16A8 8 0 0 1 8 0ZM1.5 8a6.5 6.5 0 1 0 13 0 6.5 6.5 0 0 0-13 0Zm4.75-2.75v5.5a.5.5 0 0 0 .75.433l4.5-2.75a.5.5 0 0 0 0-.866l-4.5-2.75a.5.5 0 0 0-.75.433Z"></path>
      </svg>
      <h2 style="margin: 0 0 8px 0; font-size: 16px; font-weight: 600; color: #1f2328;">RepoRunner</h2>
      <p style="margin: 0 0 16px 0; font-size: 13px; color: #656d76; line-height: 1.5;">
        LLM Chat interface coming soon!
      </p>
      <div style="padding: 12px 16px; background: #ddf4ff; border: 1px solid #54aeff66; border-radius: 6px; font-size: 12px; color: #0969da; text-align: left; line-height: 1.5;">
        <strong style="display: block; margin-bottom: 4px;">💡 Quick Start:</strong>
        Navigate to any GitHub repo with a Dockerfile or docker-compose.yml, then click the green <strong>"Run Locally"</strong> button.
      </div>
    </div>
  `;
}
