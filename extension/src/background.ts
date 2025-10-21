// background.ts - Service worker for extension
console.log('[RepoRunner] Background service worker started');

chrome.runtime.onMessage.addListener((message) => {
  console.log('[RepoRunner] Background received message:', message);
  if (message.action === 'openPopup') {
    // Popup will be opened by user clicking the action button
    // Store the repo URL for the popup to access
    console.log('[RepoRunner] Background received:', message);
  }
});

// Handle extension installation
chrome.runtime.onInstalled.addListener(() => {
  console.log('[RepoRunner] Extension installed');
});
