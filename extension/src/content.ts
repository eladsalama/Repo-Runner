// content.ts - Injects "Run Locally" button on GitHub repo pages
console.log('[RepoRunner] Content script loaded!', window.location.href);

// ============================================================================
// CUSTOMIZATION VARIABLES - Edit these to customize appearance and behavior
// ============================================================================

// Button Styling
const BUTTON_BG_COLOR = '#1f883d';              // Button background color
const BUTTON_BG_COLOR_HOVER = '#309e43ff';        // Button background on hover
const BUTTON_TEXT_COLOR = '#ffffff';            // Button text color
const BUTTON_BORDER_COLOR = 'rgba(27, 31, 36, 0.15)'; // Button border color
const BUTTON_PADDING = '3.5px 10px';              // Button padding (affects height)
const BUTTON_FONT_SIZE = '12px';                // Button text size
const BUTTON_LINE_HEIGHT = '20px';              // Button line height
const BUTTON_BORDER_RADIUS = '6px';             // Button corner roundness
const BUTTON_ICON_MARGIN = '3px';               // Space between icon and text

// Dropdown Layout
const DROPDOWN_WIDTH = '360px';                 // Dropdown width
const DROPDOWN_TOP_OFFSET = '8px';              // Distance below button
const DROPDOWN_PADDING = '16px';                // Internal section padding
const DROPDOWN_BG_COLOR = '#010409';            // Dropdown background color
const DROPDOWN_BORDER_COLOR = '#3d444d'; // Dropdown border
const DROPDOWN_BORDER_RADIUS = '12px';          // Dropdown corner roundness
const DROPDOWN_SHADOW = '0 8px 24px rgba(18, 21, 25, 0.2)'; // Dropdown shadow

// Section Dividers
const SECTION_DIVIDER_COLOR = '#2b3139';        // Line between sections

// Repository Section (top section only)
const REPO_SECTION_BG_COLOR = '#0d1117';        // Repository section background

// Labels (Repository, Mode, Primary Service)
const LABEL_FONT_SIZE = '12px';                 // Section label text size
const LABEL_COLOR = '#ffffffff';                  // Section label color
const LABEL_FONT_WEIGHT = '600';                // Section label weight
const LABEL_MARGIN_BOTTOM = '8px';              // Space below labels
const LABEL_TEXT_TRANSFORM = 'uppercase';       // Label text style
const LABEL_LETTER_SPACING = '0.05em';          // Label letter spacing

// Content Text (values under labels)
const CONTENT_FONT_SIZE = '13px';               // Content text size
const CONTENT_COLOR = '#ffffffff';                // Content text color
const CONTENT_FONT_WEIGHT = '600';              // Content weight (repo name)

// Mode Badge
const MODE_BG_COLOR = '#f78166';              // Mode badge background
const MODE_PADDING = '3px 6px';                 // Mode badge padding
const MODE_BORDER_RADIUS = '6px';               // Mode badge corners

// Primary Service Dropdown
const SELECT_BG_COLOR = '#151b23';              // Select background color
const SELECT_BORDER_COLOR = '#d0d7de';          // Select border color
const SELECT_TEXT_COLOR = '#ffffffff';            // Select text color
const SELECT_PADDING = '5px 12px';              // Select padding
const SELECT_FONT_SIZE = '14px';                // Select text size
const SELECT_BORDER_RADIUS = '6px';             // Select corner roundness

// Status Indicator - Ready/Default (Blue)
const STATUS_BG_COLOR_READY = '#151b23';        // Ready status background
const STATUS_BORDER_COLOR_READY = '#54aeff66';  // Ready status border
const STATUS_TEXT_COLOR_READY = '#0969da';      // Ready status text
const STATUS_DOT_COLOR_READY = '#0969da';       // Ready status dot

// Status Indicator - Running (Blue)
const STATUS_BG_COLOR_RUNNING = '#151b23';      // Running status background
const STATUS_BORDER_COLOR_RUNNING = '#54aeff66'; // Running status border
const STATUS_TEXT_COLOR_RUNNING = '#0550ae';    // Running status text
const STATUS_DOT_COLOR_RUNNING = '#0969da';     // Running status dot

// Status Indicator - Success (Green)
const STATUS_BG_COLOR_SUCCESS = '#151b23';      // Success status background
const STATUS_BORDER_COLOR_SUCCESS = 'rgba(26, 127, 55, 0.4)'; // Success border
const STATUS_TEXT_COLOR_SUCCESS = '#0f5323';    // Success status text
const STATUS_DOT_COLOR_SUCCESS = '#1a7f37';     // Success status dot

// Status Indicator - Failed (Red)
const STATUS_BG_COLOR_FAILED = '#151b23';       // Failed status background
const STATUS_BORDER_COLOR_FAILED = 'rgba(209, 36, 47, 0.4)'; // Failed border
const STATUS_TEXT_COLOR_FAILED = '#d1242f';     // Failed status text
const STATUS_DOT_COLOR_FAILED = '#d1242f';      // Failed status dot

// Status Indicator - Starting (Yellow)
const STATUS_BG_COLOR_STARTING = '#151b23';     // Starting status background
const STATUS_BORDER_COLOR_STARTING = 'rgba(191, 135, 0, 0.4)'; // Starting border
const STATUS_TEXT_COLOR_STARTING = '#7d4e00';   // Starting status text
const STATUS_DOT_COLOR_STARTING = '#bf8700';    // Starting status dot

// Status Box Layout
const STATUS_PADDING = '8px 12px';              // Status box padding
const STATUS_BORDER_RADIUS = '6px';             // Status box corners
const STATUS_FONT_SIZE = '13px';                // Status text size
const STATUS_MARGIN_BOTTOM = '12px';            // Space below status box
const STATUS_DOT_SIZE = '8px';                  // Status dot size
const STATUS_DOT_MARGIN = '8px';                // Space after status dot

// Action Buttons (Start Run)
const ACTION_BTN_BG_COLOR = '#1f883d';          // Start button background
const ACTION_BTN_BG_COLOR_HOVER = '#1a7f37';    // Start button hover
const ACTION_BTN_TEXT_COLOR = '#ffffff';        // Start button text
const ACTION_BTN_BORDER_COLOR = 'rgba(27, 31, 36, 0.15)'; // Start button border
const ACTION_BTN_PADDING = '5px 16px';          // Start button padding
const ACTION_BTN_FONT_SIZE = '14px';            // Start button text size
const ACTION_BTN_FONT_WEIGHT = '500';           // Start button weight
const ACTION_BTN_BORDER_RADIUS = '6px';         // Start button corners
const ACTION_BTN_ICON_MARGIN = '4px';           // Space after button icon

// Stop Button
const STOP_BTN_BG_COLOR = '#d1242f';            // Stop button background
const STOP_BTN_BG_COLOR_HOVER = '#a40e26';      // Stop button hover
const STOP_BTN_TEXT_COLOR = '#ffffff';          // Stop button text

// Error Display
const ERROR_BG_COLOR = '#fff1f0';               // Error background color
const ERROR_BORDER_COLOR = 'rgba(209, 36, 47, 0.4)'; // Error border
const ERROR_TEXT_COLOR = '#d1242f';             // Error text color
const ERROR_PADDING = '8px 12px';               // Error padding
const ERROR_FONT_SIZE = '12px';                 // Error text size
const ERROR_BORDER_RADIUS = '6px';              // Error corners
const ERROR_MARGIN_TOP = '12px';                // Space above error

// Log Display
const LOG_CONTAINER_BG_COLOR = '#0d1117';       // Log container background
const LOG_CONTAINER_BORDER_COLOR = '#3d444d';   // Log container border
const LOG_CONTAINER_HEIGHT = '200px';           // Log container height
const LOG_CONTAINER_MARGIN_TOP = '12px';        // Space above logs
const LOG_CONTAINER_BORDER_RADIUS = '6px';      // Log container corners
const LOG_HEADER_BG_COLOR = '#151b23';          // Log header background
const LOG_HEADER_PADDING = '8px 12px';          // Log header padding
const LOG_HEADER_FONT_SIZE = '12px';            // Log header text size
const LOG_HEADER_COLOR = '#ffffffff';           // Log header text color
const LOG_CONTENT_PADDING = '8px';              // Log content padding
const LOG_CONTENT_FONT_SIZE = '11px';           // Log line text size
const LOG_CONTENT_FONT_FAMILY = 'ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, "Liberation Mono", monospace'; // Log font
const LOG_LINE_COLOR = '#e6edf3';               // Log line text color
const LOG_LINE_HEIGHT = '1.4';                  // Log line height
const LOG_TAB_BG_COLOR = '#151b23';             // Log tab background
const LOG_TAB_ACTIVE_BG_COLOR = '#0d1117';      // Active log tab background
const LOG_TAB_BORDER_COLOR = '#3d444d';         // Log tab border
const LOG_TAB_PADDING = '6px 12px';             // Log tab padding
const LOG_TAB_FONT_SIZE = '12px';               // Log tab text size

// Performance Settings
const RETRY_DELAY_FAST = 50;                    // Fast retry delay (ms)
const RETRY_DELAY_SLOW = 200;                   // Slow retry delay (ms)
const MAX_INJECTION_ATTEMPTS = 20;              // Maximum injection tries
const FAST_RETRY_COUNT = 5;                     // Number of fast retries

// ============================================================================
// END CUSTOMIZATION VARIABLES
// ============================================================================

interface RepoDetection {
  hasDockerfile: boolean;
  hasCompose: boolean;
  mode: 'DOCKERFILE' | 'COMPOSE' | null;
  composePath?: string;
  services?: string[]; // Docker Compose service names
}

/**
 * Detects if repo has Dockerfile or docker-compose.yml
 */
async function detectRepoFiles(): Promise<RepoDetection> {
  const result: RepoDetection = {
    hasDockerfile: false,
    hasCompose: false,
    mode: null
  };

  // Check for docker-compose files (various naming conventions)
  const composeVariants = [
    'docker-compose.yml',
    'docker-compose.yaml',
    'compose.yml',
    'compose.yaml'
  ];

  const fileLinks = Array.from(document.querySelectorAll('a[href*="/blob/"]'));
  
  for (const link of fileLinks) {
    const href = link.getAttribute('href') || '';
    const filename = href.split('/').pop()?.toLowerCase() || '';
    
    if (filename === 'dockerfile') {
      result.hasDockerfile = true;
    }
    
    if (composeVariants.includes(filename)) {
      result.hasCompose = true;
      result.composePath = filename;
      
      // Fetch and parse docker-compose to extract service names
      try {
        const rawUrl = href.replace('/blob/', '/raw/');
        const response = await fetch(rawUrl);
        const composeText = await response.text();
        
        // Simple YAML parsing to extract top-level service names
        const servicesMatch = composeText.match(/^services:\s*$/m);
        if (servicesMatch) {
          const servicesSection = composeText.substring(servicesMatch.index! + servicesMatch[0].length);
          const serviceNames: string[] = [];
          const lines = servicesSection.split('\n');
          
          for (const line of lines) {
            // Match service names (non-indented or 2-space indented lines that end with :)
            const match = line.match(/^  ([a-z0-9_-]+):\s*$/i);
            if (match) {
              serviceNames.push(match[1]);
            } else if (line.match(/^[a-z]/i)) {
              // Hit next top-level section, stop
              break;
            }
          }
          
          if (serviceNames.length > 0) {
            result.services = serviceNames;
            console.log('[RepoRunner] Detected services:', serviceNames);
          }
        }
      } catch (err) {
        console.warn('[RepoRunner] Failed to fetch docker-compose.yml:', err);
        // Fallback to common service names
        result.services = ['web', 'app', 'api', 'frontend'];
      }
    }
  }

  // Mode priority: COMPOSE over DOCKERFILE
  if (result.hasCompose) {
    result.mode = 'COMPOSE';
  } else if (result.hasDockerfile) {
    result.mode = 'DOCKERFILE';
  }

  return result;
}

/**
 * Auto-detect primary/web service from list of services
 * Priority: web > frontend > app > client > ui > first service
 */
function detectPrimaryService(services: string[]): string {
  if (!services || services.length === 0) {
    return '';
  }
  
  const webKeywords = ['web', 'frontend', 'front-end', 'app', 'client', 'ui', 'www'];
  
  // Try to find service matching web keywords (case-insensitive)
  for (const keyword of webKeywords) {
    const match = services.find(s => s.toLowerCase().includes(keyword));
    if (match) {
      return match;
    }
  }
  
  // Fallback: return first service
  return services[0];
}

/**
 * Extract repo URL from GitHub page
 */
function getRepoUrl(): string | null {
  const match = window.location.pathname.match(/^\/([^\/]+)\/([^\/]+)/);
  if (!match) return null;
  
  const [, owner, repo] = match;
  return `https://github.com/${owner}/${repo}.git`;
}

/**
 * Injects the "Run Locally" button with dropdown
 */
function injectRunButton(detection: RepoDetection): void {
  if (document.getElementById('reporunner-container')) {
    console.log('[RepoRunner] Button already injected, skipping');
    return; // Already injected
  }

  // Find the actions bar with Watch/Fork/Star buttons
  const actionsBar = document.querySelector('.pagehead-actions') || 
                      document.querySelector('[data-hpc]');
  
  if (!actionsBar) {
    console.log('[RepoRunner] Actions bar not found, will retry');
    return;
  }
  
  console.log('[RepoRunner] Found actions bar:', actionsBar.className);

  const container = document.createElement('div');
  container.id = 'reporunner-container';
  container.style.cssText = `
    position: relative;
    display: inline-block;
    margin-left: 8px;
  `;

  // Create button matching GitHub's action buttons (Watch/Fork/Star)
  const button = document.createElement('button');
  button.id = 'reporunner-button';
  button.className = 'btn btn-sm';
  
  // Play/Run icon (triangle)
  button.innerHTML = `
    <svg aria-hidden="true" height="16" viewBox="0 0 16 16" version="1.1" width="16" style="display:inline-block;vertical-align:text-bottom;fill:currentColor;margin-right:${BUTTON_ICON_MARGIN};">
      <path d="M3 2.5v11a.5.5 0 0 0 .75.433l9-5.5a.5.5 0 0 0 0-.866l-9-5.5A.5.5 0 0 0 3 2.5z"></path>
    </svg>
    <span>Run</span>
    <svg aria-hidden="true" height="16" viewBox="0 0 16 16" version="1.1" width="16" class="dropdown-caret" style="display:inline-block;vertical-align:middle;fill:#ffffff;margin-left:${BUTTON_ICON_MARGIN};">
      <path d="M4.427 9.427l3.396 3.396a.251.251 0 0 0 .354 0l3.396-3.396A.25.25 0 0 0 11.396 9H4.604a.25.25 0 0 0-.177.427z"></path>
    </svg>
  `;
  
  // Match GitHub's button styling with customization variables
  button.style.cssText = `
    position: relative;
    display: inline-block;
    padding: ${BUTTON_PADDING};
    font-size: ${BUTTON_FONT_SIZE};
    font-weight: 500;
    line-height: ${BUTTON_LINE_HEIGHT};
    white-space: nowrap;
    vertical-align: middle;
    cursor: pointer;
    user-select: none;
    border: 1px solid ${BUTTON_BORDER_COLOR};
    border-radius: ${BUTTON_BORDER_RADIUS};
    appearance: none;
    background-color: ${BUTTON_BG_COLOR};
    color: ${BUTTON_TEXT_COLOR};
    text-align: center;
    text-decoration: none;
    transition: 0.2s cubic-bezier(0.3, 0, 0.5, 1);
  `;
  
  // Hover effect (using event listeners instead of inline handlers)
  button.addEventListener('mouseenter', () => {
    button.style.backgroundColor = BUTTON_BG_COLOR_HOVER;
  });
  button.addEventListener('mouseleave', () => {
    button.style.backgroundColor = BUTTON_BG_COLOR;
  });
  
  // Create dropdown menu matching GitHub's Code dropdown style exactly
  const dropdown = document.createElement('div');
  dropdown.id = 'reporunner-dropdown';
  dropdown.style.cssText = `
    display: none;
    position: absolute;
    top: calc(100% + ${DROPDOWN_TOP_OFFSET});
    right: 0;
    width: ${DROPDOWN_WIDTH};
    max-width: calc(100vw - 16px);
    margin-top: 2px;
    background-color: ${DROPDOWN_BG_COLOR};
    background-clip: padding-box;
    border: 1px solid ${DROPDOWN_BORDER_COLOR};
    border-radius: ${DROPDOWN_BORDER_RADIUS};
    box-shadow: ${DROPDOWN_SHADOW};
    z-index: 100;
    color: ${CONTENT_COLOR};
  `;

  const repoUrl = getRepoUrl();
  const repoName = repoUrl?.replace('.git', '').replace('https://github.com/', '') || 'Unknown';

  dropdown.innerHTML = `
    <div style="padding: ${DROPDOWN_PADDING}; background-color: ${REPO_SECTION_BG_COLOR}; border-bottom: 1px solid ${SECTION_DIVIDER_COLOR}; border-radius: ${DROPDOWN_BORDER_RADIUS} ${DROPDOWN_BORDER_RADIUS} 0 0;">
      <div style="font-size: ${LABEL_FONT_SIZE}; font-weight: ${LABEL_FONT_WEIGHT}; color: ${LABEL_COLOR}; margin-bottom: ${LABEL_MARGIN_BOTTOM}; text-transform: ${LABEL_TEXT_TRANSFORM}; letter-spacing: ${LABEL_LETTER_SPACING};">Repository</div>
      <div style="font-size: ${CONTENT_FONT_SIZE}; font-weight: ${CONTENT_FONT_WEIGHT}; color: ${CONTENT_COLOR}; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Noto Sans', Helvetica, Arial, sans-serif;">${repoName}</div>
    </div>
    <div style="padding: ${DROPDOWN_PADDING}; border-bottom: 1px solid ${SECTION_DIVIDER_COLOR};">
      <div style="font-size: ${LABEL_FONT_SIZE}; font-weight: ${LABEL_FONT_WEIGHT}; color: ${LABEL_COLOR}; margin-bottom: ${LABEL_MARGIN_BOTTOM}; text-transform: ${LABEL_TEXT_TRANSFORM}; letter-spacing: ${LABEL_LETTER_SPACING};">Build Mode</div>
      ${detection.hasCompose && detection.hasDockerfile ? `
      <div id="reporunner-mode-toggle" style="display: inline-flex; border: 1px solid #d0d7de; border-radius: 6px; overflow: hidden;">
        <button id="reporunner-mode-compose" data-mode="COMPOSE" style="
          padding: 5px 12px;
          font-size: 12px;
          font-weight: 500;
          background-color: ${detection.mode === 'COMPOSE' ? '#0969da' : '#ffffff'};
          color: ${detection.mode === 'COMPOSE' ? '#ffffff' : '#24292f'};
          border: none;
          cursor: pointer;
          transition: all 0.2s;
        ">Compose</button>
        <button id="reporunner-mode-dockerfile" data-mode="DOCKERFILE" style="
          padding: 5px 12px;
          font-size: 12px;
          font-weight: 500;
          background-color: ${detection.mode === 'DOCKERFILE' ? '#0969da' : '#ffffff'};
          color: ${detection.mode === 'DOCKERFILE' ? '#ffffff' : '#24292f'};
          border: none;
          border-left: 1px solid #d0d7de;
          cursor: pointer;
          transition: all 0.2s;
        ">Dockerfile</button>
      </div>
      ` : `
      <div style="font-size: ${CONTENT_FONT_SIZE}; color: ${CONTENT_COLOR}; font-family: ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, 'Liberation Mono', monospace; background-color: ${MODE_BG_COLOR}; padding: ${MODE_PADDING}; border-radius: ${MODE_BORDER_RADIUS}; display: inline-block;">${detection.mode}</div>
      `}
      <div id="reporunner-services-container">
      ${detection.mode === 'COMPOSE' && detection.services && detection.services.length > 0 ? `
      <div style="margin-top: 8px;">
        <div style="font-size: 11px; font-weight: ${LABEL_FONT_WEIGHT}; color: ${LABEL_COLOR}; opacity: 0.8; margin-bottom: 4px;">SERVICES (ALL WILL BE DEPLOYED)</div>
        <div style="font-size: 12px; color: ${CONTENT_COLOR}; font-family: ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, 'Liberation Mono', monospace;">
          ${detection.services.map(svc => {
            const isPrimary = svc === detectPrimaryService(detection.services!);
            return `<div style="padding: 2px 0;${isPrimary ? ' font-weight: 600;' : ''}">• ${svc}${isPrimary ? ' (primary)' : ''}</div>`;
          }).join('')}
        </div>
      </div>
      ` : ''}
      </div>
    </div>
    <div style="padding: ${DROPDOWN_PADDING};">
      <div id="reporunner-status" style="
        display: flex;
        align-items: center;
        padding: ${STATUS_PADDING};
        background-color: ${STATUS_BG_COLOR_READY};
        border: 1px solid ${STATUS_BORDER_COLOR_READY};
        border-radius: ${STATUS_BORDER_RADIUS};
        font-size: ${STATUS_FONT_SIZE};
        color: ${STATUS_TEXT_COLOR_READY};
        margin-bottom: ${STATUS_MARGIN_BOTTOM};
      ">
        <span id="reporunner-status-dot" style="
          display: inline-block;
          width: ${STATUS_DOT_SIZE};
          height: ${STATUS_DOT_SIZE};
          border-radius: 50%;
          background-color: ${STATUS_DOT_COLOR_READY};
          margin-right: ${STATUS_DOT_MARGIN};
          flex-shrink: 0;
        "></span>
        <span id="reporunner-status-text">Ready</span>
      </div>
      <button id="reporunner-start-btn" style="
        width: 100%;
        display: inline-flex;
        align-items: center;
        justify-content: center;
        padding: ${ACTION_BTN_PADDING};
        font-size: ${ACTION_BTN_FONT_SIZE};
        font-weight: ${ACTION_BTN_FONT_WEIGHT};
        line-height: 20px;
        white-space: nowrap;
        cursor: pointer;
        user-select: none;
        border: 1px solid ${ACTION_BTN_BORDER_COLOR};
        border-radius: ${ACTION_BTN_BORDER_RADIUS};
        background-color: ${ACTION_BTN_BG_COLOR};
        color: ${ACTION_BTN_TEXT_COLOR};
        box-shadow: 0 1px 0 rgba(27, 31, 36, 0.04), inset 0 1px 0 hsla(0, 0%, 100%, 0.25);
        transition: 0.2s cubic-bezier(0.3, 0, 0.5, 1);
      ">
        <svg aria-hidden="true" height="16" viewBox="0 0 16 16" version="1.1" width="16" style="display:inline-block;vertical-align:text-bottom;fill:currentColor;margin-right:${ACTION_BTN_ICON_MARGIN};">
          <path d="M3 2.5v11a.5.5 0 0 0 .75.433l9-5.5a.5.5 0 0 0 0-.866l-9-5.5A.5.5 0 0 0 3 2.5z"></path>
        </svg>
        Start Run
      </button>
      <button id="reporunner-stop-btn" style="
        display: none;
        width: 100%;
        padding: ${ACTION_BTN_PADDING};
        font-size: ${ACTION_BTN_FONT_SIZE};
        font-weight: ${ACTION_BTN_FONT_WEIGHT};
        line-height: 20px;
        cursor: pointer;
        border: 1px solid ${ACTION_BTN_BORDER_COLOR};
        border-radius: ${ACTION_BTN_BORDER_RADIUS};
        background-color: ${STOP_BTN_BG_COLOR};
        color: ${STOP_BTN_TEXT_COLOR};
        box-shadow: 0 1px 0 rgba(27, 31, 36, 0.04);
        transition: 0.2s cubic-bezier(0.3, 0, 0.5, 1);
      ">
        Stop Run
      </button>
      <div id="reporunner-error" style="
        display: none;
        margin-top: ${ERROR_MARGIN_TOP};
        padding: ${ERROR_PADDING};
        background-color: ${ERROR_BG_COLOR};
        border: 1px solid ${ERROR_BORDER_COLOR};
        border-radius: ${ERROR_BORDER_RADIUS};
        font-size: ${ERROR_FONT_SIZE};
        color: ${ERROR_TEXT_COLOR};
      "></div>
      <div id="reporunner-run-id" style="
        margin-top: 16px;
        font-size: 11px;
        font-family: ui-monospace, SFMono-Regular, 'SF Mono', Consolas, 'Liberation Mono', Menlo, monospace;
        color: #57606a;
        word-break: break-all;
        display: none;
      ">
        <span style="color: #8c959f;">Run ID:</span> <span id="reporunner-run-id-value"></span>
      </div>
    </div>
  `;

  // Add event listeners for buttons to avoid CSP violations (no inline handlers)
  const startBtn = dropdown.querySelector('#reporunner-start-btn') as HTMLButtonElement;
  const stopBtn = dropdown.querySelector('#reporunner-stop-btn') as HTMLButtonElement;
  
  // Start button hover effect
  if (startBtn) {
    startBtn.addEventListener('mouseenter', () => {
      startBtn.style.backgroundColor = ACTION_BTN_BG_COLOR_HOVER;
    });
    startBtn.addEventListener('mouseleave', () => {
      startBtn.style.backgroundColor = ACTION_BTN_BG_COLOR;
    });
    startBtn.addEventListener('click', async () => {
      await startRun(detection, repoUrl);
    });
  }
  
  // Stop button hover effect
  if (stopBtn) {
    stopBtn.addEventListener('mouseenter', () => {
      stopBtn.style.backgroundColor = STOP_BTN_BG_COLOR_HOVER;
    });
    stopBtn.addEventListener('mouseleave', () => {
      stopBtn.style.backgroundColor = STOP_BTN_BG_COLOR;
    });
    stopBtn.addEventListener('click', async () => {
      await stopRun();
    });
  }

  // Mode toggle buttons (if both Compose and Dockerfile exist)
  const composeBtn = dropdown.querySelector('#reporunner-mode-compose') as HTMLButtonElement;
  const dockerfileBtn = dropdown.querySelector('#reporunner-mode-dockerfile') as HTMLButtonElement;
  
  if (composeBtn && dockerfileBtn) {
    composeBtn.addEventListener('click', () => {
      detection.mode = 'COMPOSE';
      composeBtn.style.backgroundColor = '#0969da';
      composeBtn.style.color = '#ffffff';
      dockerfileBtn.style.backgroundColor = '#ffffff';
      dockerfileBtn.style.color = '#24292f';
      
      // Reload services display
      const servicesContainer = dropdown.querySelector('#reporunner-services-container');
      if (servicesContainer && detection.services && detection.services.length > 0) {
        servicesContainer.innerHTML = `
          <div style="margin-top: 8px;">
            <div style="font-size: 11px; font-weight: ${LABEL_FONT_WEIGHT}; color: ${LABEL_COLOR}; opacity: 0.8; margin-bottom: 4px;">SERVICES (ALL WILL BE DEPLOYED)</div>
            <div style="font-size: 12px; color: ${CONTENT_COLOR}; font-family: ui-monospace, SFMono-Regular, 'SF Mono', Menlo, Consolas, 'Liberation Mono', monospace;">
              ${detection.services.map(svc => {
                const isPrimary = svc === detectPrimaryService(detection.services!);
                return `<div style="padding: 2px 0;${isPrimary ? ' font-weight: 600;' : ''}">• ${svc}${isPrimary ? ' (primary)' : ''}</div>`;
              }).join('')}
            </div>
          </div>
        `;
      }
    });
    
    dockerfileBtn.addEventListener('click', () => {
      detection.mode = 'DOCKERFILE';
      dockerfileBtn.style.backgroundColor = '#0969da';
      dockerfileBtn.style.color = '#ffffff';
      composeBtn.style.backgroundColor = '#ffffff';
      composeBtn.style.color = '#24292f';
      
      // Hide services display
      const servicesContainer = dropdown.querySelector('#reporunner-services-container');
      if (servicesContainer) {
        servicesContainer.innerHTML = '';
      }
    });
  }

  // Toggle dropdown
  button.addEventListener('click', (e) => {
    e.stopPropagation();
    const isVisible = dropdown.style.display === 'block';
    dropdown.style.display = isVisible ? 'none' : 'block';
  });

  // Close dropdown when clicking outside
  document.addEventListener('click', (e) => {
    if (!container.contains(e.target as Node)) {
      dropdown.style.display = 'none';
    }
  });

  container.appendChild(button);
  container.appendChild(dropdown);
  
  // Prepend to actions bar (will appear before Watch/Fork/Star buttons)
  actionsBar.prepend(container);
  console.log('[RepoRunner] Button injected successfully!');
  
  // Verify visibility
  setTimeout(() => {
    const rect = button.getBoundingClientRect();
    console.log('[RepoRunner] Button position:', { 
      top: rect.top, 
      left: rect.left, 
      width: rect.width, 
      height: rect.height,
      visible: rect.width > 0 && rect.height > 0
    });
  }, 100);
}

/**
 * Start a run from the dropdown
 */
let currentRunId: string | null = null;
let statusPollInterval: number | null = null;
const GATEWAY_URL = 'http://localhost:5247';

async function startRun(detection: RepoDetection, repoUrl: string | null): Promise<void> {
  if (!repoUrl) return;

  const statusEl = document.getElementById('reporunner-status')!;
  const statusDot = document.getElementById('reporunner-status-dot')!;
  const statusText = document.getElementById('reporunner-status-text')!;
  const errorEl = document.getElementById('reporunner-error')!;
  const startBtn = document.getElementById('reporunner-start-btn')! as HTMLButtonElement;
  const stopBtn = document.getElementById('reporunner-stop-btn')!;
  
  // Auto-detect primary service (no manual selection needed)
  const primaryService = detection.services && detection.services.length > 0
    ? detectPrimaryService(detection.services)
    : undefined;

  try {
    errorEl.style.display = 'none';
    statusText.textContent = 'Starting Gateway service...';
    statusDot.style.backgroundColor = STATUS_DOT_COLOR_STARTING;
    statusEl.style.backgroundColor = STATUS_BG_COLOR_STARTING;
    statusEl.style.borderColor = STATUS_BORDER_COLOR_STARTING;
    statusEl.style.color = STATUS_TEXT_COLOR_STARTING;
    startBtn.disabled = true;

    // Check if Gateway is healthy before starting run
    try {
      const healthCheck = await fetch(`${GATEWAY_URL}/health`, { method: 'GET' });
      if (!healthCheck.ok) {
        throw new Error('Gateway service not responding');
      }
    } catch (healthError) {
      throw new Error('⚠️ RepoRunner services are not running.\n\nStart them with: .\\scripts\\start-background.ps1');
    }

    statusText.textContent = 'Starting run...';

    const response = await fetch(`${GATEWAY_URL}/api/runs/start`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        repoUrl: repoUrl,
        branch: 'main',
        mode: detection.mode,
        composePath: detection.composePath,
        primaryService: primaryService
      })
    });

    if (!response.ok) {
      throw new Error(`HTTP ${response.status}: ${response.statusText}`);
    }

    const result = await response.json();
    currentRunId = result.runId;

    // Show stop button, hide start button
    startBtn.style.display = 'none';
    stopBtn.style.display = 'block';

    // Show run ID at bottom
    const runIdContainer = document.getElementById('reporunner-run-id')!;
    const runIdValue = document.getElementById('reporunner-run-id-value')!;
    runIdValue.textContent = currentRunId;
    runIdContainer.style.display = 'block';

    // Start polling status
    pollStatus(statusEl, statusDot, statusText, startBtn, stopBtn);

  } catch (error) {
    errorEl.textContent = `Failed to start: ${(error as Error).message}`;
    errorEl.style.display = 'block';
    statusText.textContent = 'Failed';
    statusDot.style.backgroundColor = STATUS_DOT_COLOR_FAILED;
    statusEl.style.backgroundColor = STATUS_BG_COLOR_FAILED;
    statusEl.style.borderColor = STATUS_BORDER_COLOR_FAILED;
    statusEl.style.color = STATUS_TEXT_COLOR_FAILED;
    startBtn.disabled = false;
  }
}

/**
 * Stop a run
 */
async function stopRun(): Promise<void> {
  if (!currentRunId) return;

  const statusEl = document.getElementById('reporunner-status');
  const statusDot = document.getElementById('reporunner-status-dot');
  const statusText = document.getElementById('reporunner-status-text');
  const errorEl = document.getElementById('reporunner-error');
  const runIdEl = document.getElementById('reporunner-run-id');

  // Guard against missing elements
  if (!statusEl || !statusDot || !statusText || !errorEl || !runIdEl) {
    console.error('[RepoRunner] Stop failed: Required DOM elements not found');
    return;
  }

  try {
    // Call stop API
    await fetch(`${GATEWAY_URL}/api/runs/${currentRunId}/stop`, {
      method: 'POST'
    });

    // Update status to "Stopped"
    statusText.textContent = 'Stopped';
    statusDot.style.backgroundColor = STATUS_DOT_COLOR_READY;
    statusEl.style.backgroundColor = STATUS_BG_COLOR_READY;
    statusEl.style.borderColor = STATUS_BORDER_COLOR_READY;
    statusEl.style.color = STATUS_TEXT_COLOR_READY;
    
    // Stop polling
    if (statusPollInterval) {
      clearInterval(statusPollInterval);
      statusPollInterval = null;
    }
    
    // Wait a moment to show "Stopped" status
    await new Promise(resolve => setTimeout(resolve, 1000));
    
    // Reset to "Ready" state
    statusText.textContent = 'Ready';
    errorEl.style.display = 'none';
    errorEl.textContent = '';
    runIdEl.style.display = 'none';
    
    // Reset buttons (removes preview button, shows start button)
    resetButtons();

  } catch (error) {
    if (errorEl) {
      errorEl.textContent = `Failed to stop: ${(error as Error).message}`;
      errorEl.style.display = 'block';
    }
  }
}

/**
 * Poll run status
 */
let previewUrlOpened = false; // Track if preview URL has been opened
let animationFrame = 0; // For dot animation

function pollStatus(statusEl: HTMLElement, statusDot: HTMLElement, statusText: HTMLElement, startBtn: HTMLElement, stopBtn: HTMLElement): void {
  if (statusPollInterval) {
    clearInterval(statusPollInterval);
  }

  previewUrlOpened = false; // Reset for new run
  animationFrame = 0;

  statusPollInterval = window.setInterval(async () => {
    if (!currentRunId) {
      clearInterval(statusPollInterval!);
      return;
    }

    try {
      const response = await fetch(`${GATEWAY_URL}/api/runs/${currentRunId}/status`);

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }

      const status = await response.json();
      
      // Animate dots for building status (. -> .. -> ... -> . -> ..)
      const dots = ['.', '..', '...'];
      const currentDots = dots[animationFrame % 3];
      animationFrame++;
      
      // Display build progress if available with animation
      if (status.buildProgress) {
        statusText.textContent = `${status.status} ${currentDots} (${status.buildProgress})`;
      } else {
        statusText.textContent = `${status.status} ${currentDots}`;
      }

      // Update colors based on status
      if (status.status === 'Succeeded') {
        statusDot.style.backgroundColor = STATUS_DOT_COLOR_SUCCESS;
        statusEl.style.backgroundColor = STATUS_BG_COLOR_SUCCESS;
        statusEl.style.borderColor = STATUS_BORDER_COLOR_SUCCESS;
        statusEl.style.color = STATUS_TEXT_COLOR_SUCCESS;
        statusText.textContent = 'Succeeded ✓';
        
        // Show "Open Preview" button
        showPreviewButton(status.previewUrl);
        
        // Stop polling after SUCCEEDED
        clearInterval(statusPollInterval!);
      } else if (status.status === 'Running') {
        statusDot.style.backgroundColor = STATUS_DOT_COLOR_RUNNING;
        statusEl.style.backgroundColor = STATUS_BG_COLOR_RUNNING;
        statusEl.style.borderColor = STATUS_BORDER_COLOR_RUNNING;
        statusEl.style.color = STATUS_TEXT_COLOR_RUNNING;
        statusText.textContent = 'Running ✓';
        
        // Show "Open Preview" button
        showPreviewButton(status.previewUrl);
        
        clearInterval(statusPollInterval!);
      } else if (status.status === 'Failed') {
        statusDot.style.backgroundColor = STATUS_DOT_COLOR_FAILED;
        statusEl.style.backgroundColor = STATUS_BG_COLOR_FAILED;
        statusEl.style.borderColor = STATUS_BORDER_COLOR_FAILED;
        statusEl.style.color = STATUS_TEXT_COLOR_FAILED;
        statusText.textContent = 'Failed ✗';
        clearInterval(statusPollInterval!);
        resetButtons(); // Don't hide Run ID on failure
      }

    } catch (error) {
      console.error('Status poll error:', error);
    }
  }, 2000);
}

/**
 * Show preview button when deployment succeeds
 */
function showPreviewButton(previewUrl: string | null): void {
  if (!previewUrl) return;
  
  const startBtn = document.getElementById('reporunner-start-btn')! as HTMLButtonElement;
  const stopBtn = document.getElementById('reporunner-stop-btn')!;
  
  // Hide start button only
  startBtn.style.display = 'none';
  
  // Keep stop button visible so user can cleanup
  stopBtn.style.display = 'inline-flex';
  
  // Create or update preview button
  let previewBtn = document.getElementById('reporunner-preview-btn') as HTMLButtonElement | null;
  if (!previewBtn) {
    previewBtn = document.createElement('button');
    previewBtn.id = 'reporunner-preview-btn';
    previewBtn.innerHTML = `
      <svg aria-hidden="true" height="16" viewBox="0 0 16 16" version="1.1" width="16" style="display:inline-block;vertical-align:text-bottom;fill:currentColor;margin-right:4px;">
        <path d="M8 0a8 8 0 1 1 0 16A8 8 0 0 1 8 0ZM1.5 8a6.5 6.5 0 1 0 13 0 6.5 6.5 0 0 0-13 0Zm9.78-2.22-5.5 5.5a.749.749 0 0 1-1.275-.326.749.749 0 0 1 .215-.734l5.5-5.5a.751.751 0 0 1 1.042.018.751.751 0 0 1 .018 1.042Z"></path>
      </svg>
      Open Preview
    `;
    previewBtn.style.cssText = `
      width: 100%;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      padding: ${ACTION_BTN_PADDING};
      font-size: ${ACTION_BTN_FONT_SIZE};
      font-weight: ${ACTION_BTN_FONT_WEIGHT};
      line-height: 20px;
      white-space: nowrap;
      cursor: pointer;
      user-select: none;
      border: 1px solid ${ACTION_BTN_BORDER_COLOR};
      border-radius: ${ACTION_BTN_BORDER_RADIUS};
      background-color: ${ACTION_BTN_BG_COLOR};
      color: ${ACTION_BTN_TEXT_COLOR};
      box-shadow: 0 1px 0 rgba(27, 31, 36, 0.04), inset 0 1px 0 hsla(0, 0%, 100%, 0.25);
      transition: 0.2s cubic-bezier(0.3, 0, 0.5, 1);
      margin-bottom: 8px;
    `;
    
    previewBtn.addEventListener('mouseenter', () => {
      previewBtn!.style.backgroundColor = ACTION_BTN_BG_COLOR_HOVER;
    });
    previewBtn.addEventListener('mouseleave', () => {
      previewBtn!.style.backgroundColor = ACTION_BTN_BG_COLOR;
    });
    
    previewBtn.addEventListener('click', () => {
      window.open(previewUrl, '_blank');
    });
    
    // Insert before stop button (so preview is above stop)
    stopBtn.parentNode!.insertBefore(previewBtn, stopBtn);
  }
  
  previewBtn.style.display = 'inline-flex';
}

/**
 * Create log tabs for COMPOSE mode
 */
let currentLogService: string | null = null;

function createLogTabs(detection: RepoDetection, primaryService: string | undefined): void {
  const tabsContainer = document.getElementById('reporunner-logs-tabs')!;
  tabsContainer.innerHTML = '';

  // For COMPOSE mode, create tabs for build, run-all, and per-service
  if (detection.mode === 'COMPOSE') {
    const tabs = [
      { label: 'All', value: null },
      { label: 'Build', value: 'build' },
      { label: 'Run', value: 'run' }
    ];

    // Add primary service tab if available
    if (primaryService) {
      tabs.push({ label: primaryService, value: `service:${primaryService}` });
    }

    tabs.forEach((tab, index) => {
      const button = document.createElement('button');
      button.textContent = tab.label;
      button.style.cssText = `
        padding: ${LOG_TAB_PADDING};
        font-size: ${LOG_TAB_FONT_SIZE};
        background-color: ${index === 0 ? LOG_TAB_ACTIVE_BG_COLOR : LOG_TAB_BG_COLOR};
        border: 1px solid ${LOG_TAB_BORDER_COLOR};
        border-radius: 6px;
        color: ${LOG_HEADER_COLOR};
        cursor: pointer;
        transition: background-color 0.2s;
      `;

      button.addEventListener('click', () => {
        // Update active tab
        tabsContainer.querySelectorAll('button').forEach(btn => {
          (btn as HTMLElement).style.backgroundColor = LOG_TAB_BG_COLOR;
        });
        button.style.backgroundColor = LOG_TAB_ACTIVE_BG_COLOR;

        // Re-stream logs with filter
        if (currentRunId) {
          const logsContent = document.getElementById('reporunner-logs-content')!;
          logsContent.innerHTML = '';
          
          if (tab.value?.startsWith('service:')) {
            const serviceName = tab.value.substring(8);
            streamLogs(currentRunId, serviceName);
          } else {
            streamLogs(currentRunId, null);
          }
        }
      });

      tabsContainer.appendChild(button);
    });
  } else {
    // For DOCKERFILE mode, just show Build and Run tabs
    const tabs = [
      { label: 'All', value: null },
      { label: 'Build', value: 'build' },
      { label: 'Run', value: 'run' }
    ];

    tabs.forEach((tab, index) => {
      const button = document.createElement('button');
      button.textContent = tab.label;
      button.style.cssText = `
        padding: ${LOG_TAB_PADDING};
        font-size: ${LOG_TAB_FONT_SIZE};
        background-color: ${index === 0 ? LOG_TAB_ACTIVE_BG_COLOR : LOG_TAB_BG_COLOR};
        border: 1px solid ${LOG_TAB_BORDER_COLOR};
        border-radius: 6px;
        color: ${LOG_HEADER_COLOR};
        cursor: pointer;
        transition: background-color 0.2s;
      `;

      button.addEventListener('click', () => {
        // Update active tab
        tabsContainer.querySelectorAll('button').forEach(btn => {
          (btn as HTMLElement).style.backgroundColor = LOG_TAB_BG_COLOR;
        });
        button.style.backgroundColor = LOG_TAB_ACTIVE_BG_COLOR;

        // Re-stream logs
        if (currentRunId) {
          const logsContent = document.getElementById('reporunner-logs-content')!;
          logsContent.innerHTML = '';
          streamLogs(currentRunId, null);
        }
      });

      tabsContainer.appendChild(button);
    });
  }
}

/**
 * Stream logs from Gateway using REST polling (gRPC-Web streaming deferred)
 * Note: This is a simplified REST-based polling approach for MVP
 * Real implementation will use gRPC-Web StreamLogs with server-streaming
 */
async function streamLogs(runId: string, serviceName: string | null): Promise<void> {
  currentLogService = serviceName;
  const logsContent = document.getElementById('reporunner-logs-content')!;
  
  // Clear existing logs
  logsContent.textContent = 'Loading logs...\n';

  // Poll for logs every 2 seconds (simplified approach for MVP)
  // Real implementation: Use gRPC-Web StreamLogs with server-streaming
  let lastLogCount = 0;
  
  const logPollInterval = setInterval(async () => {
    if (!currentRunId || currentRunId !== runId) {
      clearInterval(logPollInterval);
      return;
    }

    try {
      // Note: This REST endpoint is not yet implemented in Gateway
      // For now, just show placeholder text
      // Real implementation will call StreamLogs gRPC method
      const url = serviceName 
        ? `${GATEWAY_URL}/api/runs/${runId}/logs?service=${serviceName}`
        : `${GATEWAY_URL}/api/runs/${runId}/logs`;
      
      // Placeholder: In real implementation, this will fetch from Gateway REST endpoint
      // or use gRPC-Web StreamLogs
      // For MVP, we show a message indicating logs are being collected
      if (lastLogCount === 0) {
        logsContent.textContent = `📝 Logs are being collected by Runner and Builder services.\n\n` +
          `Once the gRPC-Web bridge is set up, logs will stream here in real-time.\n\n` +
          `Check Gateway, Builder, and Runner service logs for actual output.\n\n` +
          `Run ID: ${runId}\n` +
          (serviceName ? `Service: ${serviceName}\n` : '');
        lastLogCount = 1;
      }

    } catch (error) {
      console.error('Log fetch error:', error);
      if (lastLogCount === 0) {
        logsContent.textContent = `⚠️  Unable to fetch logs. Check that Gateway is running.\n\n` +
          `Error: ${(error as Error).message}`;
        lastLogCount = 1;
      }
    }
  }, 2000);

  // Stop log polling when run completes
  setTimeout(() => {
    clearInterval(logPollInterval);
  }, 300000); // Stop after 5 minutes
}

/**
 * Reset buttons to initial state
 */
function resetButtons(): void {
  const startBtn = document.getElementById('reporunner-start-btn');
  const stopBtn = document.getElementById('reporunner-stop-btn');
  const previewBtn = document.getElementById('reporunner-preview-btn');
  const logsContainer = document.getElementById('reporunner-logs-container');
  const errorEl = document.getElementById('reporunner-error');
  
  // Show start button, hide stop button
  if (startBtn) {
    startBtn.style.display = 'block';
    (startBtn as HTMLButtonElement).disabled = false;
  }
  if (stopBtn) {
    stopBtn.style.display = 'none';
  }
  
  // Remove preview button if it exists
  if (previewBtn) {
    previewBtn.remove();
  }
  
  // Hide error message
  if (errorEl) {
    errorEl.style.display = 'none';
    errorEl.textContent = '';
  }
  
  // Never hide Run ID - keep it visible for debugging/reference
  
  // Hide logs when run completes
  if (logsContainer) {
    logsContainer.style.display = 'none';
  }
  
  currentLogService = null;
}

// Main execution - inject immediately and retry if needed
(function main() {
  let attemptCount = 0;
  const maxAttempts = MAX_INJECTION_ATTEMPTS;
  
  async function tryInject() {
    attemptCount++;
    const detection = await detectRepoFiles();
    
    if (detection.mode) {
      console.log('[RepoRunner] Detected mode:', detection.mode);
      injectRunButton(detection);
      
      // Check if button was successfully injected
      if (document.getElementById('reporunner-container')) {
        console.log('[RepoRunner] Button injection successful on attempt', attemptCount);
        return;
      }
    }
    
    // Retry if not yet successful and within max attempts (faster retries)
    if (attemptCount < maxAttempts) {
      const delay = attemptCount < FAST_RETRY_COUNT ? RETRY_DELAY_FAST : RETRY_DELAY_SLOW;
      setTimeout(tryInject, delay);
    } else {
      console.log('[RepoRunner] Failed to inject button after', maxAttempts, 'attempts');
    }
  }
  
  // Try immediately on different loading stages
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', tryInject);
  } else {
    tryInject(); // DOM already loaded
  }
  
  // Also listen for GitHub's pjax navigation
  document.addEventListener('pjax:end', () => {
    console.log('[RepoRunner] PJAX navigation detected, re-checking...');
    attemptCount = 0;
    setTimeout(tryInject, 50);
  });
  
  // Use MutationObserver for instant detection of actions bar
  const observer = new MutationObserver((mutations) => {
    if (document.getElementById('reporunner-container')) {
      return; // Already injected
    }
    
    for (const mutation of mutations) {
      if (mutation.addedNodes.length > 0) {
        const actionsBar = document.querySelector('.pagehead-actions');
        if (actionsBar && !document.getElementById('reporunner-container')) {
          detectRepoFiles().then(detection => {
            if (detection.mode) {
              injectRunButton(detection);
              observer.disconnect(); // Stop observing once injected
            }
          });
          break;
        }
      }
    }
  });
  
  observer.observe(document.body, { childList: true, subtree: true });
})();
