(() => {
  const icons = {
    grid: '<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',
    activity: '<path d="M3 12h4l2-7 4 14 2-7h6"/>',
    sliders: '<path d="M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3"/><path d="M1 14h6M9 8h6M17 16h6"/>',
    'folder-open': '<path d="M3 7V5a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v2"/><path d="M3 7h17l-2 11H5L3 7Z"/>',
    'arrow-up-right': '<path d="M7 17 17 7M8 7h9v9"/>',
    'refresh-cw': '<path d="M21 12a9 9 0 0 1-15.5 6.2L3 16"/><path d="M3 21v-5h5"/><path d="M3 12A9 9 0 0 1 18.5 5.8L21 8"/><path d="M21 3v5h-5"/>',
    download: '<path d="M12 3v12m0 0 4-4m-4 4-4-4"/><path d="M4 17v3h16v-3"/>',
    'file-archive': '<path d="M6 3h9l5 5v13H6z"/><path d="M14 3v6h6"/><path d="M10 7h2M10 10h2M10 13h2M10 16h2"/>',
    layers: '<path d="m12 3 9 5-9 5-9-5 9-5Z"/><path d="m3 12 9 5 9-5"/><path d="m3 16 9 5 9-5"/>',
    'arrow-right': '<path d="M5 12h14m-5-5 5 5-5 5"/>',
    'chevron-left': '<path d="m15 18-6-6 6-6"/>',
    'chevron-right': '<path d="m9 18 6-6-6-6"/>',
    'file-text': '<path d="M14 3H6v18h12V7l-4-4Z"/><path d="M14 3v5h4M9 13h6M9 17h6"/>',
    database: '<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v7c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12v7c0 1.7 3.6 3 8 3s8-1.3 8-3v-7"/>',
    'gamepad-2': '<path d="M6 12h4M8 10v4M15 13h.01M18 11h.01"/><rect x="2" y="6" width="20" height="12" rx="5"/>',
    palette: '<path d="M12 3a9 9 0 1 0 0 18h1.5a2.5 2.5 0 0 0 0-5H12a2 2 0 0 1 0-4h2a5 5 0 0 0 0-10Z"/><circle cx="7.5" cy="10" r="1"/><circle cx="9" cy="6" r="1"/><circle cx="14" cy="6" r="1"/>',
    play: '<path d="m8 5 11 7-11 7V5Z"/>',
    save: '<path d="M5 3h12l3 3v15H4V3h1Z"/><path d="M8 3v6h8V3M8 21v-7h8v7"/>',
    'arrow-down-to-line': '<path d="M12 3v12m0 0 4-4m-4 4-4-4"/><path d="M4 20h16"/>',
    'calendar-days': '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 10h18"/><path d="M8 14h.01M12 14h.01M16 14h.01M8 18h.01M12 18h.01"/>',
    search: '<circle cx="11" cy="11" r="7"/><path d="m20 20-4-4"/>',
    calendar: '<rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 10h18"/>',
    'music-4': '<path d="M9 18V5l10-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="16" cy="16" r="3"/>',
    square: '<rect x="5" y="5" width="14" height="14" rx="1"/>',
    x: '<path d="M6 6l12 12M18 6 6 18"/>',
    'folder-search-2': '<path d="M3 7V5a2 2 0 0 1 2-2h4l2 2h6a2 2 0 0 1 2 2v3"/><circle cx="14" cy="15" r="4"/><path d="m17 18 3 3"/>',
    'wand-sparkles': '<path d="m15 4 1-2 1 2 2 1-2 1-1 2-1-2-2-1 2-1Z"/><path d="m4 20 10-10 2 2L6 22l-2-2Z"/><path d="m14 3 1 1"/>',
    gauge: '<path d="M20.5 15a9 9 0 1 0-17 0"/><path d="m12 14 4-4"/><circle cx="12" cy="15" r="1"/>',
    info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v5M12 8h.01"/>',
    'shield-check': '<path d="M12 3 4 6v6c0 5 3.5 8 8 9 4.5-1 8-4 8-9V6l-8-3Z"/><path d="m8.5 12 2.3 2.3 4.7-5"/>',
    unarchive: '<path d="M4 9h16v11H4z"/><path d="M3 3h18v6H3zM10 13h4"/>',
    check: '<path d="m5 12 4 4L19 6"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
    alert: '<path d="M12 3 2 21h20L12 3Z"/><path d="M12 9v5M12 17h.01"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    'rotate-cw': '<path d="M21 12a9 9 0 1 1-3-6.7L21 8"/><path d="M21 3v5h-5"/>',
    'cloud-off': '<path d="M3 3l18 18"/><path d="M5.8 5.9A7 7 0 0 0 6 19h11.5"/><path d="M18.4 16.3A5 5 0 0 0 18 6.5a7 7 0 0 0-9.8-1.1"/>',
    play: '<path d="m8 5 11 7-11 7V5Z"/>',
    'external-link': '<path d="M14 3h7v7M10 14 21 3M21 14v7H3V3h7"/>'
  };

  const $ = (selector, root = document) => root.querySelector(selector);
  const $$ = (selector, root = document) => [...root.querySelectorAll(selector)];
  const post = (type, payload = {}) => window.chrome.webview.postMessage({ type, payload });

  const state = {
    folder: null,
    catalog: [],
    catalogMeta: { total: 0, filtered: 0, downloaded: 0, newCount: 0, page: 0, pageCount: 1, pageSize: 24 },
    statusFilter: 'all',
    search: '',
    gameStatus: null,
    gameSettings: null,
    onlineSources: [],
    onlineSourceTesting: false,
    skins: { online: [], installed: [] },
    gameUpdates: 0,
    dateFrom: '',
    dateTo: '',
    concurrency: 6,
    includeVideo: true,
    autoCheck: true,
    downloadMode: 'auto',
    useMirrors: true,
    storageTarget: 'both',
    busy: false,
    indexing: false,
    tasks: new Map(),
    zipPath: '',
    operation: { total: 0, completed: 0 }
  };

  function hydrateIcons(root = document) {
    $$('[data-icon]', root).forEach((node) => {
      if (node.dataset.hydrated === '1') return;
      node.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${icons[node.dataset.icon] || ''}</svg>`;
      node.dataset.hydrated = '1';
    });
  }

  function escapeHtml(value = '') {
    return String(value).replace(/[&<>"']/g, (char) => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[char]);
  }

  function formatRelative(value) {
    if (!value) return '—';
    const then = new Date(value).getTime();
    if (!Number.isFinite(then)) return '—';
    const seconds = Math.max(0, (Date.now() - then) / 1000);
    if (seconds < 60) return '刚刚';
    if (seconds < 3600) return `${Math.floor(seconds / 60)} 分钟前`;
    if (seconds < 86400) return `${Math.floor(seconds / 3600)} 小时前`;
    if (seconds < 86400 * 7) return `${Math.floor(seconds / 86400)} 天前`;
    return new Date(value).toLocaleDateString('zh-CN');
  }

  function formatNumber(value) {
    return Number(value || 0).toLocaleString('zh-CN');
  }

  function showToast(message, kind = 'info') {
    const toast = document.createElement('div');
    toast.className = `toast ${kind}`;
    toast.innerHTML = `<span data-icon="${kind === 'success' ? 'check' : kind === 'error' || kind === 'warning' ? 'alert' : 'info'}"></span><span>${escapeHtml(message)}</span>`;
    $('#toastStack').appendChild(toast);
    hydrateIcons(toast);
    setTimeout(() => {
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(12px)';
      setTimeout(() => toast.remove(), 220);
    }, 4200);
  }

  function switchView(view) {
    $$('.nav-item').forEach((item) => item.classList.toggle('active', item.dataset.view === view));
    $$('.view').forEach((item) => item.classList.toggle('active', item.id === `view-${view}`));
    const titles = {
      library: ['MAJDATA · 曲目小屋', '曲目库'],
      tasks: ['MAJDATA · DOWNLOADS', '下载队列'],
      settings: ['MAJDATA · PREFERENCES', '偏好设置'],
      game: ['MAJDATA · GAME MANAGER', '游戏管理'],
      skins: ['MAJDATA · SKINS', '皮肤']
    };
    $('#viewEyebrow').textContent = titles[view][0];
    $('#viewTitle').textContent = titles[view][1];
    document.querySelector('.main').scrollTo({ top: 0, behavior: 'smooth' });
    if (view === 'game') {
      post('gameStatus');
      post('loadGameSettings');
    } else if (view === 'skins') {
      post('loadSkins');
    }
  }

  function updateFolder(folder) {
    state.folder = folder;
    const ready = !!folder?.detected;
    $('#pathValue').textContent = ready ? folder.rootPath : '尚未选择曲目目录';
    $('#pathDot').classList.toggle('ok', ready);
    $('#settingsPath').textContent = ready ? folder.rootPath : '未识别';
    $('#connectionText').textContent = ready ? '目录已连接' : '本地目录待识别';
    $('#connectionPill .status-dot').classList.toggle('ok', ready);
    $('#statLocal').textContent = ready ? formatNumber(folder.folderCount) : '—';
    if (!ready) {
      openModal('tourModal');
    }
  }

  function requestCatalogPage(page = 0) {
    state.catalogMeta.page = Math.max(0, page);
    post('queryCatalog', {
      page: state.catalogMeta.page,
      pageSize: 24,
      search: state.search || null,
      status: state.statusFilter,
      dateFrom: state.dateFrom || null,
      dateTo: state.dateTo || null
    });
  }

  function renderCatalog() {
    const grid = $('#chartGrid');
    const items = state.catalog;
    const meta = state.catalogMeta;
    const newCount = meta.newCount || 0;
    $('#navLibraryCount').textContent = meta.total ? formatNumber(meta.total) : '—';
    $('#newCountBadge').textContent = `${newCount} NEW`;
    $('#statCharts').textContent = meta.total ? formatNumber(meta.total) : '—';
    $('#heroRingValue').textContent = meta.total ? `${newCount} 首待同步` : '同步';
    $('#catalogSummary').textContent = meta.total
      ? `筛选结果 ${meta.filtered ? formatNumber(meta.filtered) : '0'} 首 · 已入库 ${formatNumber(meta.downloaded || 0)} 首`
      : '尚未读取索引';
    $('#catalogEmpty').classList.toggle('hidden', items.length > 0);
    grid.innerHTML = items.map((item) => {
      const stateClass = item.stale ? 'stale' : item.downloaded ? 'downloaded' : '';
      return `
        <article class="chart-card ${stateClass}" data-id="${escapeHtml(item.id)}">
          <div class="chart-cover">
            <img src="${escapeHtml(item.image)}" alt="${escapeHtml(item.title)}" loading="lazy" referrerpolicy="no-referrer"
              onerror="this.style.display='none'">
            <span class="cover-label">${escapeHtml((item.tags || [])[0] || 'MAJDATA')}</span>
            <span class="cover-state ${item.downloaded && !item.stale ? '' : 'new'}" title="${item.stale ? '有更新' : item.downloaded ? '已入库' : '未下载'}">
              <span data-icon="${item.stale ? 'rotate-cw' : item.downloaded ? 'check' : 'download'}"></span>
            </span>
            <button class="chart-download" data-download="${escapeHtml(item.id)}" title="${item.downloaded ? '重新下载' : '下载此谱面'}" aria-label="下载">
              <span data-icon="${item.downloaded ? 'refresh-cw' : 'download'}"></span>
            </button>
          </div>
          <div class="chart-body">
            <h4 class="chart-title" title="${escapeHtml(item.title)}">${escapeHtml(item.title)}</h4>
            <div class="chart-artist" title="${escapeHtml(item.artist)}">${escapeHtml(item.artist)}</div>
            <div class="chart-info-line">
              <span class="level-text">${escapeHtml(item.levelText)}</span>
              <span class="date-text">${escapeHtml((item.dateText || '').slice(0, 10))}</span>
            </div>
          </div>
        </article>`;
    }).join('');
    hydrateIcons(grid);
    renderPager();
  }

  function renderPager() {
    const meta = state.catalogMeta;
    const pager = $('#catalogPager');
    pager.classList.toggle('hidden', !meta.total || meta.pageCount <= 1);
    $('#pagePrev').disabled = meta.page <= 0;
    $('#pageNext').disabled = meta.page >= meta.pageCount - 1;
    $('#pageCaption').textContent = `第 ${meta.page + 1} / ${meta.pageCount} 页`;

    const pages = [];
    const add = (value) => {
      if (!pages.includes(value)) pages.push(value);
    };
    const start = Math.max(0, meta.page - 2);
    const end = Math.min(meta.pageCount - 1, meta.page + 2);
    for (let i = 0; i <= Math.min(1, meta.pageCount - 1); i++) add(i);
    for (let i = start; i <= end; i++) add(i);
    for (let i = Math.max(0, meta.pageCount - 2); i < meta.pageCount; i++) add(i);
    pages.sort((a, b) => a - b);
    const parts = [];
    pages.forEach((page, index) => {
      if (index > 0 && page - pages[index - 1] > 1) parts.push('<span class="ellipsis">…</span>');
      parts.push(`<button data-page="${page}" class="${page === meta.page ? 'active' : ''}">${page + 1}</button>`);
    });
    $('#pageButtons').innerHTML = parts.join('');
    hydrateIcons(pager);
  }

  function renderRecent(items) {
    const row = $('#recentRow');
    if (!items || !items.length) {
      row.innerHTML = '<div class="muted-note">完成下载后，最近入库的曲目会显示在这里。</div>';
      return;
    }
    row.innerHTML = items.map((item) => `
      <div class="recent-item">
        <div class="recent-thumb">${item.image
          ? `<img src="${escapeHtml(item.image)}" alt="" onerror="this.style.display='none'">`
          : '<span data-icon="music-4"></span>'}</div>
        <div class="recent-copy">
          <strong title="${escapeHtml(item.title)}">${escapeHtml(item.title)}</strong>
          <span>${escapeHtml(item.artist || item.folder)} · ${formatRelative(item.downloadedAt)}</span>
        </div>
        <button class="icon-button recent-cache" data-cache-id="${escapeHtml(item.id || '')}" title="写入在线缓存" aria-label="写入在线缓存">
          <span data-icon="database"></span>
        </button>
      </div>`).join('');
    hydrateIcons(row);
  }

  function renderTask(task) {
    state.tasks.set(task.id, { ...(state.tasks.get(task.id) || {}), ...task });
    $('#navTaskCount').textContent = state.tasks.size;
    const list = $('#taskList');
    $('#taskEmpty')?.classList.add('hidden');
    let row = $(`.task-row[data-task-id="${CSS.escape(task.id)}"]`, list);
    if (!row) {
      row = document.createElement('div');
      row.className = 'task-row';
      row.dataset.taskId = task.id;
      list.prepend(row);
    }
    const percent = Math.max(0, Math.min(100, Math.round((task.progress || 0) * 100)));
    const statusIcon = task.state === 'done' ? 'check' : task.state === 'error' ? 'alert' : 'refresh-cw';
    const statusText = task.state === 'done' ? '已完成' : task.state === 'error' ? '失败' : '下载中';
    row.innerHTML = `
      <div class="task-status ${escapeHtml(task.state)}"><span data-icon="${statusIcon}"></span></div>
      <div class="task-copy">
        <strong title="${escapeHtml(task.title)}">${escapeHtml(task.title)}</strong>
        <span title="${escapeHtml(task.subtitle || '')}">${escapeHtml(task.subtitle || '正在准备')}</span>
      </div>
      <div class="task-progress">
        <div class="progress-track"><div style="width:${percent}%"></div></div>
        <small>${percent}%</small>
      </div>
      <div class="task-state-text">${statusText}</div>`;
    hydrateIcons(row);
  }

  function renderGameStatus(payload) {
    state.gameStatus = payload;
    const installed = !!payload.installed;
    $('#gamePathText').textContent = payload.gameRoot
      ? (installed ? '游戏已安装，可以启动或检查更新' : '已选择安装目录，可以下载游戏')
      : '尚未选择游戏目录';
    $('#gameFolderPath').textContent = payload.gameRoot || '未设置';
    $('#gameSource').value = payload.source || 'cnb';
    $('#installGame').disabled = false;
    $('#installGame').innerHTML = installed
      ? '<span data-icon="rotate-cw"></span> 重新安装'
      : '<span data-icon="download"></span> 下载安装';
    hydrateIcons($('#installGame'));
    $('#installGameHint').textContent = installed
      ? '当前已安装；重新下载会覆盖游戏文件，但保留曲目与皮肤。'
      : '下载后自动解压到所选游戏目录';
    $('#launchGame').disabled = !installed;
    $('#checkGameUpdate').disabled = !installed;
    $('#saveGameSettings').disabled = !installed;
    $('#loadAdvancedSettings').disabled = !installed;
    $('#saveAdvancedSettings').disabled = !installed;
    renderInstalledSkins(payload.skins || []);
  }

  function startOnlineSourceTest() {
    if (state.onlineSourceTesting) return;
    state.onlineSourceTesting = true;
    $('#gameSettingsGrid .online-test')?.setAttribute('disabled', '');
    post('testOnlineSources');
  }

  function renderGameSettings(settings) {
    state.gameSettings = settings;
    const grid = $('#gameSettingsGrid');
    if (!settings) {
      grid.innerHTML = '<div class="muted-note">运行一次游戏后，settings.json 会出现在游戏目录中。</div>';
      $('#advancedSettingsGrid').innerHTML = '';
      $('#loadAdvancedSettings').disabled = true;
      $('#saveAdvancedSettings').disabled = true;
      $('#advancedSettings').open = false;
      return;
    }

    $('#loadAdvancedSettings').disabled = false;
    if ($('#advancedSettings').open && !$('#advancedSettingsGrid').children.length) {
      renderAdvancedSettings(settings);
    }

    const groups = {
      Game: ['TapSpeed', 'TouchSpeed', 'BackgroundDim', 'StarRotation', 'TrackSkip', 'FastRetry'],
      Judge: ['AudioOffset', 'JudgeOffset', 'AnswerOffset', 'TouchPanelOffset', 'Mode'],
      Display: ['FastLateType', 'DisplayCriticalPerfect', 'DisplayBreakScore', 'FPSLimit', 'VSync'],
      Audio: ['ForceMono', 'Backend'],
      Online: ['Enable', 'UseProxy', 'Proxy']
    };
    const labels = {
      TapSpeed: 'Tap 速度', TouchSpeed: 'Touch 速度', BackgroundDim: '背景暗化',
      StarRotation: '星星旋转', TrackSkip: '允许跳过前奏', FastRetry: '快速重试',
      AudioOffset: '音频偏移', JudgeOffset: '判定偏移', AnswerOffset: '应答音偏移',
      TouchPanelOffset: '触摸面板偏移', Mode: '判定模式', FastLateType: 'Fast/Late 提示',
      DisplayCriticalPerfect: '显示 Critical Perfect', DisplayBreakScore: '显示 Break 分数',
      FPSLimit: 'FPS 限制', VSync: '垂直同步', ForceMono: '强制单声道',
      Backend: '音频后端', Enable: '在线功能', UseProxy: '使用代理', Proxy: '代理地址'
    };
    const choices = {
      Mode: ['Classic', 'Modern'],
      FastLateType: ['All', 'BelowCP', 'BelowP', 'BelowGR', 'MissOnly', 'Disable'],
      Backend: ['Wasapi', 'Asio']
    };

    grid.innerHTML = Object.entries(groups).map(([section, keys]) => {
      if (!settings[section]) return '';
      const onlineEndpoint = section === 'Online'
        ? settings.Online?.ApiEndpoints?.[0]?.Url || ''
        : '';
      const endpointOptions = [
        ['https://majdata.net/api3/api/', '本站 · majdata.net'],
        ['https://maj-1.moyingmoe.top/api3/api/', '镜像一 · moyingmoe'],
        ['https://maj-2.moyingmoe.top/api3/api/', '镜像二 · moyingmoe'],
        ['https://maj-3.moyingmoe.top/api3/api/', '镜像三 · moyingmoe']
      ];
      return `<div class="game-settings-group">
        <h4>${section}</h4>
        ${section === 'Online' ? renderOnlineSources(onlineEndpoint, endpointOptions) : ''}
        ${keys.filter(key => key in settings[section]).map(key => {
          const value = settings[section][key];
          const id = `setting-${section}-${key}`;
          let input;
          if (typeof value === 'boolean') {
            input = `<input type="checkbox" data-setting-section="${section}" data-setting-key="${key}" ${value ? 'checked' : ''}>`;
          } else if (choices[key]) {
            input = `<select data-setting-section="${section}" data-setting-key="${key}">${choices[key].map(option =>
              `<option value="${escapeHtml(option)}" ${option === value ? 'selected' : ''}>${escapeHtml(option)}</option>`).join('')}</select>`;
          } else {
            const type = typeof value === 'number' ? 'number' : 'text';
            const step = typeof value === 'number' ? 'step="0.01"' : '';
            input = `<input id="${id}" type="${type}" ${step} value="${escapeHtml(value ?? '')}" data-setting-section="${section}" data-setting-key="${key}">`;
          }
          return `<label class="game-setting-field"><span>${escapeHtml(labels[key] || key)}</span>${input}</label>`;
        }).join('')}
      </div>`;
    }).join('');
    hydrateIcons(grid);
    startOnlineSourceTest();
  }

  const builtinSettingKeys = {
    Game: new Set(['TapSpeed', 'TouchSpeed', 'BackgroundDim', 'StarRotation', 'TrackSkip', 'FastRetry']),
    Judge: new Set(['AudioOffset', 'JudgeOffset', 'AnswerOffset', 'TouchPanelOffset', 'Mode']),
    Display: new Set(['FastLateType', 'DisplayCriticalPerfect', 'DisplayBreakScore', 'FPSLimit', 'VSync']),
    Audio: new Set(['ForceMono', 'Backend']),
    Online: new Set(['Enable', 'UseProxy', 'Proxy', 'ApiEndpoints'])
  };

  function flattenAdvancedFields(value, path = [], output = []) {
    if (value && typeof value === 'object' && !Array.isArray(value)) {
      for (const [key, child] of Object.entries(value)) {
        const excluded = path.length === 0
          ? false
          : path.length === 1
            ? builtinSettingKeys[path[0]]?.has(key)
            : false;
        if (!excluded) flattenAdvancedFields(child, [...path, key], output);
      }
      return output;
    }
    output.push({ path, value });
    return output;
  }

  function renderAdvancedSettings(settings) {
    const grid = $('#advancedSettingsGrid');
    const fields = flattenAdvancedFields(settings);
    const metadata = window.MAJ_SETTING_METADATA || {};
    const labels = metadata.labels || {};
    const descriptions = metadata.descriptions || {};
    const extraLabels = metadata.extraLabels || {};
    const extraDescriptions = metadata.extraDescriptions || {};
    const enums = metadata.enums || {};
    const ranges = metadata.ranges || {};
    const defaults = metadata.defaults || {};
    const pathRanges = metadata.pathRanges || {};
    const pathDefaults = metadata.pathDefaults || {};
    const grouped = new Map();
    for (const field of fields) {
      const section = field.path[0] || 'Root';
      if (!grouped.has(section)) grouped.set(section, []);
      grouped.get(section).push(field);
    }

    grid.innerHTML = [...grouped].map(([section, items]) => `
      <section class="advanced-settings-group">
        <h4>${escapeHtml(section)} <span>${items.length}</span></h4>
        ${items.map(({ path, value }) => {
          const key = path.join('.');
          const leaf = path[path.length - 1];
          const labelLookup = path[0] === 'Audio' && path.length > 1 && path[1] === 'Volume'
            ? `Audio.Volume.${leaf}`
            : leaf;
          const label = labels[labelLookup] || labels[leaf] ||
            extraLabels[labelLookup] || extraLabels[leaf] ||
            path.slice(1).join('.') || path[0];
          let description = (descriptions[leaf] || extraDescriptions[labelLookup] || extraDescriptions[leaf] || '')
            .replace(/\\n/g, ' ');
          if (description.trim().toLowerCase() === 'nothing') description = '';
          const options = enums[leaf];
          const range = pathRanges[key] || ranges[leaf];
          const id = `advanced-${path.join('-').replace(/[^a-zA-Z0-9_-]/g, '_')}`;
          let input;
          if (options) {
            input = `<select id="${id}" data-advanced-path="${escapeHtml(key)}">
              ${options.map(option => option === null
                ? `<option value="null" ${value === null ? 'selected' : ''}>null（自动）</option>`
                : `<option value="${escapeHtml(option)}" ${String(value) === option ? 'selected' : ''}>${escapeHtml(option)}</option>`).join('')}
            </select>`;
          } else if (typeof value === 'boolean') {
            input = `<input type="checkbox" id="${id}" data-advanced-path="${escapeHtml(key)}" ${value ? 'checked' : ''}>`;
          } else if (typeof value === 'number') {
            input = `<input type="number"
              ${range?.step != null ? `step="${range.step}"` : 'step="any"'}
              ${range?.min != null ? `min="${range.min}"` : ''}
              ${range?.max != null ? `max="${range.max}"` : ''}
              id="${id}" data-advanced-path="${escapeHtml(key)}" value="${escapeHtml(value)}">`;
          } else if (value !== null && typeof value === 'object') {
            input = `<textarea id="${id}" data-advanced-path="${escapeHtml(key)}" data-advanced-json="1" spellcheck="false">${escapeHtml(JSON.stringify(value, null, 2))}</textarea>`;
          } else {
            input = `<input type="text" id="${id}" data-advanced-path="${escapeHtml(key)}" value="${escapeHtml(value ?? 'null')}">`;
          }
          const hintParts = [];
          if (description) hintParts.push(description);
          if (range) {
            const bounds = range.min != null && range.max != null
              ? `范围 ${range.min}–${range.max}`
              : range.min != null
                ? `最小值 ${range.min}`
                : range.max != null
                  ? `最大值 ${range.max}`
                  : '';
            if (bounds) hintParts.push(bounds);
            if (range.step != null) hintParts.push(`步长 ${range.step}`);
            if (range.unit) hintParts.push(range.unit);
          }
          const hasDefault = Object.prototype.hasOwnProperty.call(pathDefaults, key) ||
            Object.prototype.hasOwnProperty.call(defaults, leaf);
          if (hasDefault) {
            const defaultValue = Object.prototype.hasOwnProperty.call(pathDefaults, key)
              ? pathDefaults[key]
              : defaults[leaf];
            hintParts.push(`默认 ${defaultValue === null ? 'null' : defaultValue}`);
          }
          if (options) hintParts.push(`有效值：${options.map(x => x ?? 'null').join(' / ')}`);
          return `<div class="advanced-setting-field ${typeof value === 'boolean' ? 'check' : ''}"
            data-search-text="${escapeHtml(`${key} ${key.split('.').join(' ')} ${label} ${hintParts.join(' ')}`)}">
            <label for="${id}" title="${escapeHtml(key)}">${escapeHtml(label)}
              <span>${escapeHtml(key)}</span>
            </label>
            ${input}
            ${hintParts.length ? `<small class="advanced-setting-help">${escapeHtml(hintParts.join(' · '))}</small>` : ''}
          </div>`;
        }).join('')}
      </section>`).join('');
    hydrateIcons(grid);
    $('#advancedSettingsSearch').value = '';
    $('#advancedSearchCount').textContent = '输入名称或分组快速定位';
    $('#advancedSettingsHint').textContent =
      `已读取 ${fields.length} 个未在内置设置中展示的字段。`;
    $('#saveAdvancedSettings').disabled = false;
  }

  const advancedSearchAliases = [
    ['音量', ['audio.volume', 'volume']],
    ['皮肤', ['display.skin', 'skin']],
    ['判定', ['judge', 'judgeoffset']],
    ['延迟', ['offset', 'latency']],
    ['输入', ['io.input', 'inputdevice']],
    ['输出', ['io.output', 'outputdevice']],
    ['调试', ['debug']],
    ['帧率', ['fpslimit', 'fps']],
    ['分辨率', ['resolution']]
  ];

  function filterAdvancedSettings(rawQuery) {
    const query = rawQuery.trim().toLowerCase();
    const alias = advancedSearchAliases.find(([label]) => query.includes(label.toLowerCase()))?.[1] || [];
    const terms = query
      ? [query, ...alias].filter((value, index, list) => value && list.indexOf(value) === index)
      : [];
    let visible = 0;
    $$('#advancedSettingsGrid .advanced-setting-field').forEach(field => {
      const text = `${field.dataset.searchText || ''} ${field.textContent}`.toLowerCase();
      const match = terms.length === 0 || terms.some(term => text.includes(term));
      field.classList.toggle('hidden', !match);
      if (match) visible++;
    });
    $$('#advancedSettingsGrid .advanced-settings-group').forEach(group => {
      const empty = ![...group.querySelectorAll('.advanced-setting-field')]
        .some(field => !field.classList.contains('hidden'));
      group.classList.toggle('hidden', empty);
    });
    $('#advancedSearchCount').textContent = terms.length
      ? `找到 ${visible} 个设置`
      : '输入名称或分组快速定位';
    if (terms.length && visible > 0) {
      const first = $$('#advancedSettingsGrid .advanced-setting-field')
        .find(field => !field.classList.contains('hidden'));
      first?.scrollIntoView({ block: 'center', behavior: 'smooth' });
      first?.classList.add('search-hit');
      setTimeout(() => first?.classList.remove('search-hit'), 1100);
    }
  }

  function setAdvancedValue(target, path, value) {
    let current = target;
    for (let index = 0; index < path.length - 1; index++) {
      const key = path[index];
      if (!current[key] || typeof current[key] !== 'object' || Array.isArray(current[key])) {
        current[key] = {};
      }
      current = current[key];
    }
    current[path[path.length - 1]] = value;
  }

  function collectAdvancedSettings() {
    const settings = JSON.parse(JSON.stringify(state.gameSettings || {}));
    let count = 0;
    $$('#advancedSettingsGrid [data-advanced-path]').forEach(input => {
      const path = input.dataset.advancedPath.split('.');
      let value;
      if (input.type === 'checkbox') {
        value = input.checked;
      } else if (input.type === 'number') {
        if (input.value === '' || !Number.isFinite(Number(input.value))) {
          throw new Error(`字段 ${input.dataset.advancedPath} 不是有效数字`);
        }
        value = Number(input.value);
      } else if (input.dataset.advancedJson === '1') {
        try {
          value = JSON.parse(input.value);
        } catch {
          throw new Error(`字段 ${input.dataset.advancedPath} 的 JSON 格式错误`);
        }
      } else if (input.value === 'null') {
        value = null;
      } else {
        value = input.value;
      }
      setAdvancedValue(settings, path, value);
      count++;
    });
    return { settings, count };
  }

  function renderOnlineSources(selected, options) {
    const byHost = new Map((state.onlineSources || []).map(source => [source.host, source]));
    const cards = options.map(([url, label], index) => {
      const host = new URL(url).host;
      const measurement = byHost.get(host);
      const latency = measurement?.available && measurement.latencyMs != null
        ? `${Math.round(measurement.latencyMs)} ms`
        : measurement ? '不可用' : '待测速';
      const tone = measurement?.available === false ? 'error' : measurement ? 'ok' : '';
      return `<button class="online-source-card ${url === selected ? 'selected' : ''} ${tone}"
        data-online-source-url="${escapeHtml(url)}">
        <span class="online-source-name">${escapeHtml(label)}</span>
        <span class="online-source-latency">${latency}</span>
      </button>`;
    }).join('');
    return `<div class="online-source-panel">
      <div class="online-source-head">
        <span>在线下载源</span>
        <button class="button ghost online-test" id="testOnlineSources"><span data-icon="gauge"></span> 重新测速</button>
      </div>
      <div class="online-source-grid">${cards}</div>
      <input type="hidden" data-online-source value="${escapeHtml(selected)}">
      <div class="online-source-note" id="onlineSourceNote">${state.onlineSources?.length
        ? '延迟越低通常越适合当前网络。'
        : '点击重新测速，比较本站与三个镜像的连接延迟。'}</div>
      ${selected && !options.some(([url]) => url === selected)
        ? `<div class="online-source-custom">当前为自定义源：${escapeHtml(selected)}</div>`
        : ''}
    </div>`;
  }

  function renderOnlineSourceLatency(payload) {
    state.onlineSources = payload.sources || [];
    const selected = $('#gameSettingsGrid [data-online-source]')?.value || '';
    if (state.gameSettings?.Online) {
      const container = $('#gameSettingsGrid .game-settings-group:last-child');
      if (!container) return;
      const panel = container.querySelector('.online-source-panel');
      if (panel) {
        panel.outerHTML = renderOnlineSources(selected, [
          ['https://majdata.net/api3/api/', '本站 · majdata.net'],
          ['https://maj-1.moyingmoe.top/api3/api/', '镜像一 · moyingmoe'],
          ['https://maj-2.moyingmoe.top/api3/api/', '镜像二 · moyingmoe'],
          ['https://maj-3.moyingmoe.top/api3/api/', '镜像三 · moyingmoe']
        ]);
        hydrateIcons($('#gameSettingsGrid'));
      }
    }
    state.onlineSourceTesting = false;
    $('#gameSettingsGrid .online-test')?.removeAttribute('disabled');
    if (payload.error) {
      const note = $('#onlineSourceNote');
      if (note) note.textContent = '测速失败：' + payload.error;
    }
  }

  function renderSkins(payload) {
    state.skins = payload;
    const online = payload.online || [];
    const installed = payload.installed || [];
    $('#onlineSkinCount').textContent = `${online.length} 个在线皮肤`;
    $('#skinSourceNote').innerHTML = '<span data-icon="info"></span><span>在线列表来自 CNB，失败时自动切换 GitHub。</span>';
    hydrateIcons($('#skinSourceNote'));
    $('#skinGrid').innerHTML = online.length ? online.map(skin => `
      <article class="skin-card">
        <div class="skin-preview" aria-hidden="true">
          <span class="skin-preview-ring"></span>
          <span class="skin-preview-star">✦</span>
          <span class="skin-preview-label">SKIN</span>
        </div>
        <div class="skin-card-body">
          <div class="skin-card-title">
            <strong title="${escapeHtml(skin.name)}">${escapeHtml(skin.name)}</strong>
            <span class="skin-state ${skin.installed ? 'installed' : ''}">${skin.installed ? '已安装' : '可下载'}</span>
          </div>
          <span class="skin-meta">${skin.size ? (skin.size / 1024 / 1024).toFixed(1) + ' MB' : '在线皮肤'} · MajdataPlay 皮肤包</span>
          <button class="button ghost skin-action" data-skin-name="${escapeHtml(skin.name)}" data-skin-file="${escapeHtml(skin.fileName)}" data-skin-size="${skin.size || 0}">
            <span data-icon="${skin.installed ? 'rotate-cw' : 'download'}"></span> ${skin.installed ? '重新安装' : '下载皮肤'}
          </button>
        </div>
      </article>`).join('') : `
      <div class="skin-empty">
        <span data-icon="cloud-off"></span>
        <strong>暂时没有读取到在线皮肤</strong>
        <span>请检查网络后点击右上角刷新列表。</span>
      </div>`;
    renderInstalledSkins(installed);
    hydrateIcons($('#skinGrid'));
  }

  function renderInstalledSkins(skins) {
    const row = $('#installedSkins');
    if (!skins.length) {
      row.innerHTML = '<div class="muted-note">还没有安装皮肤。</div>';
      return;
    }
    row.innerHTML = skins.map(skin => `
      <div class="recent-item installed-skin-item ${skin.active ? 'active' : ''}">
        <div class="recent-thumb installed-skin-thumb"><span data-icon="palette"></span></div>
        <div class="recent-copy">
          <strong>${escapeHtml(skin.name)}</strong>
          <span>${skin.active ? '当前正在使用' : '安装在 Skins 目录'}</span>
        </div>
        <button class="button ghost skin-apply" data-apply-skin="${escapeHtml(skin.name)}"
          ${skin.active ? 'disabled' : ''} title="${skin.active ? '当前皮肤' : '应用这个皮肤'}">
          <span data-icon="${skin.active ? 'check' : 'play'}"></span> ${skin.active ? '使用中' : '应用'}
        </button>
      </div>`).join('');
    hydrateIcons(row);
  }

  function setOperation(payload) {
    const bar = $('#operationBar');
    const { stage, message, total, completed, indeterminate } = payload;
    if (stage === 'done' || stage === 'error' || stage === 'cancelled') {
      $('#operationText').textContent = message || '操作结束';
      $('#operationProgress').style.width = stage === 'done' ? '100%' : $('#operationProgress').style.width;
      $('#footerStatus').textContent = stage === 'error' ? 'ERROR' : stage === 'cancelled' ? 'STOPPED' : 'READY';
      setTimeout(() => {
        if (!state.busy) bar.classList.remove('show');
      }, 2200);
      $('#queueStage').textContent = stage === 'done' ? '已完成' : stage === 'error' ? '出错' : '已停止';
      $('#cancelOperation').disabled = true;
      return;
    }
    state.busy = true;
    bar.classList.add('show');
    $('#operationText').textContent = message || '正在处理';
    $('#operationCount').textContent = total ? `${completed || 0} / ${total}` : '';
    $('#queueStage').textContent = stage === 'index' ? '读取索引' : stage === 'import' ? '导入解压' : '并行下载';
    $('#queueCompleted').textContent = total ? `${completed || 0} / ${total}` : '—';
    $('#cancelOperation').disabled = false;
    if (total) state.operation = { total, completed: completed || 0 };
    if (indeterminate) {
      $('#operationProgress').style.width = '37%';
      $('#operationProgress').style.animation = 'pulse-bar 1.2s ease-in-out infinite';
    } else {
      $('#operationProgress').style.animation = '';
      const percent = total ? Math.min(100, ((completed || 0) / total) * 100) : 0;
      $('#operationProgress').style.width = `${percent}%`;
      $('#queueProgress').style.width = `${percent}%`;
    }
    if (payload.succeeded !== undefined) {
      $('#queueResult').textContent = `成功 ${payload.succeeded} · 失败 ${payload.failed} · 跳过 ${payload.skipped}`;
    }
    $('#footerStatus').textContent = stage === 'index' ? 'INDEXING' : 'TRANSFERRING';
  }

  function setBusy(running) {
    state.busy = running;
    $('#syncNew').disabled = running;
    $('#downloadAll').disabled = running;
    $('#refreshIndex').disabled = running;
    if (!running) {
      $('#operationBar').classList.remove('show');
      $('#operationProgress').style.animation = '';
      $('#cancelOperation').disabled = true;
    }
  }

  function settingsPayload() {
    return {
      concurrency: Number($('#concurrencyRange').value || state.concurrency || 6),
      includeVideo: $('#includeVideo').checked,
      autoCheck: $('#settingsAutoCheck').checked,
      downloadMode: $('#downloadMode').value || state.downloadMode
      ,useMirrors: $('#useMirrors').checked
      ,storageTarget: state.storageTarget
    };
  }

  function requestSync(updatedOnly, extra = {}) {
    if (!state.folder?.detected) {
      showToast('请先选择 Majdata 的 Original 文件夹', 'warning');
      openModal('tourModal');
      return;
    }
    $('#taskList').querySelectorAll('.task-row').forEach((row) => row.remove());
    state.tasks.clear();
    $('#navTaskCount').textContent = '0';
    $('#taskEmpty').classList.remove('hidden');
    $('#queueResult').textContent = '任务进行中';
    post('sync', {
      updatedOnly,
      chartId: extra.chartId || null,
      dateFrom: extra.dateFrom || state.dateFrom || null,
      dateTo: extra.dateTo || state.dateTo || null,
      search: state.search || null,
      limit: extra.limit || null,
      includeVideo: $('#includeVideo').checked,
      concurrency: Number($('#concurrencyRange').value || 6),
      storageTarget: state.storageTarget
    });
    switchView('tasks');
  }

  function openModal(id) {
    const modal = document.getElementById(id);
    if (!modal) return;
    modal.classList.remove('hidden');
    if (id === 'tourModal' && state.folder?.detected) {
      modal.classList.add('hidden');
    }
  }

  function closeModal(modal) {
    modal.classList.add('hidden');
  }

  function setDateFields(from, to) {
    $('#modalDateFrom').value = from;
    $('#modalDateTo').value = to;
    $('#dateFrom').value = from;
    $('#dateTo').value = to;
    state.dateFrom = from;
    state.dateTo = to;
  }

  function updateConcurrency(value) {
    const n = Math.max(1, Math.min(12, Number(value) || 6));
    $('#concurrencyRange').value = n;
    $('#concurrencyValue').textContent = n;
    $('#settingsConcurrency').textContent = n;
    state.concurrency = n;
    post('saveSettings', {
      concurrency: n,
      includeVideo: $('#includeVideo').checked,
      autoCheck: $('#settingsAutoCheck').checked,
      downloadMode: $('#downloadMode').value || state.downloadMode
      ,useMirrors: $('#useMirrors').checked
    });
  }

  function updateNetworkModeUi() {
    const labels = { auto: 'AUTO', single: '单个连接', '2': '双连接', '4': '四连接' };
    $('#networkModeBadge').textContent = labels[state.downloadMode] || 'AUTO';
    $('#downloadMode').value = state.downloadMode;
    if (!$('#networkCardText').textContent || $('#networkCardText').textContent.includes('自动选择')) {
      $('#networkCardText').textContent = state.downloadMode === 'single'
        ? '节点自动优选；大文件按单连接下载。'
        : '自动选择低延迟 CDN 节点，大文件分段下载。';
    }
  }

  function renderNetworkRoutes(routes, message) {
    const list = $('#networkRouteList');
    $('#networkNodeStatus').textContent = message;
    if (!routes.length) {
      list.classList.add('hidden');
      $('#networkCardText').textContent = '自动选择低延迟站点；下载失败时自动切换。';
      return;
    }

    list.classList.remove('hidden');
    list.innerHTML = routes.map((route, index) => `
      <div class="network-route ${index === 0 ? 'fastest' : ''}">
        <strong>${escapeHtml(route.host)}</strong>
        <span class="route-latency">${Number(route.latencyMs).toLocaleString('zh-CN')} ms</span>
        <span class="route-ip" title="${escapeHtml((route.addresses || []).join(', '))}">
          ${escapeHtml((route.addresses || []).slice(0, 2).join(' · ') || '无可用 IP')}
        </span>
      </div>`).join('');
    $('#networkCardText').textContent =
      `优先 ${routes[0].host}（${routes[0].latencyMs} ms），站点失败时自动切换。`;
  }

  function bootstrapEvents() {
    $$('.nav-item').forEach((button) => button.addEventListener('click', () => switchView(button.dataset.view)));
    $('#chooseFolder').addEventListener('click', () => post('chooseFolder'));
    $('#settingsChoose').addEventListener('click', () => post('chooseFolder'));
    $('#detectFolder').addEventListener('click', () => post('detectFolder'));
    $('#openFolder').addEventListener('click', () => post('openFolder'));
    $('#openFolderSettings').addEventListener('click', () => post('openFolder'));
    $('#openLogs').addEventListener('click', () => post('openLogs'));
    $('#refreshIndex').addEventListener('click', () => post('loadIndex'));
    $('#emptyLoad').addEventListener('click', () => post('loadIndex'));
    $('#syncNew').addEventListener('click', () => requestSync(true));
    $('#cardSync').addEventListener('click', () => requestSync(true));
    $('#cardDate').addEventListener('click', () => openModal('dateModal'));
    $('#downloadAll').addEventListener('click', () => {
      if (!confirm('将下载站点当前索引中的全部谱面，可能需要较长时间和较大磁盘空间。确定继续吗？')) return;
      requestSync(false);
    });
    $('#cardImport').addEventListener('click', () => openModal('importModal'));
    $('#cardNetwork').addEventListener('click', () => {
      switchView('settings');
      $('#testNetwork').focus();
    });
    $('#cardCacheAll').addEventListener('click', () => {
      if (!confirm('将已有的本地曲目写入 MajdataPlay 在线缓存，继续吗？')) return;
      post('syncCacheAll');
      switchView('tasks');
    });
    $('#recentRow').addEventListener('click', (event) => {
      const button = event.target.closest('[data-cache-id]');
      if (button?.dataset.cacheId) {
        post('importCache', { id: button.dataset.cacheId });
        switchView('tasks');
      }
    });
    $('#importZip').addEventListener('click', () => openModal('importModal'));
    $('#filterButton').addEventListener('click', () => $('#filterDrawer').classList.toggle('open'));
    $('#clearFilters').addEventListener('click', () => {
      setDateFields('', '');
      $('#filterDrawer').classList.remove('open');
      requestCatalogPage(0);
    });
    $('#applyDateSync').addEventListener('click', () => {
      requestSync(false, {
        dateFrom: $('#dateFrom').value || null,
        dateTo: $('#dateTo').value || null
      });
    });
    $('#dateFrom').addEventListener('change', () => { state.dateFrom = $('#dateFrom').value; requestCatalogPage(0); });
    $('#dateTo').addEventListener('change', () => { state.dateTo = $('#dateTo').value; requestCatalogPage(0); });
    $('#concurrencyRange').addEventListener('input', (event) => {
      $('#concurrencyValue').textContent = event.target.value;
    });
    $('#concurrencyRange').addEventListener('change', (event) => updateConcurrency(event.target.value));
    $('#includeVideo').addEventListener('change', () => post('saveSettings', settingsPayload()));
    $('#settingsVideo').addEventListener('change', () => {
      $('#includeVideo').checked = $('#settingsVideo').checked;
      post('saveSettings', {
        concurrency: state.concurrency,
        includeVideo: $('#settingsVideo').checked,
        autoCheck: $('#settingsAutoCheck').checked
      });
    });
    $('#settingsAutoCheck').addEventListener('change', () => post('saveSettings', {
      concurrency: state.concurrency,
      includeVideo: $('#settingsVideo').checked,
      autoCheck: $('#settingsAutoCheck').checked
    }));
    $('#downloadMode').addEventListener('change', () => {
      state.downloadMode = $('#downloadMode').value;
      updateNetworkModeUi();
      post('saveSettings', settingsPayload());
      showToast('下载模式已保存：' + $('#downloadMode').selectedOptions[0].textContent, 'success');
    });
    $('#testNetwork').addEventListener('click', () => {
      $('#testNetwork').disabled = true;
      $('#networkNodeStatus').textContent = '正在测速…';
      post('testNetwork');
    });
    $('#useMirrors').addEventListener('change', () => {
      state.useMirrors = $('#useMirrors').checked;
      post('saveSettings', settingsPayload());
      showToast(
        state.useMirrors ? '已优先使用加速镜像' : '已切换为仅主站下载',
        'success');
    });
    $('#storageTarget').addEventListener('change', () => {
      state.storageTarget = $('#storageTarget').value;
      $('#settingsStorageTarget').value = state.storageTarget;
      post('saveSettings', settingsPayload());
      const labels = {
        both: '本地 + 在线缓存',
        cache: '仅写入 MajdataPlay 在线缓存',
        local: '仅写入本地曲目库'
      };
      showToast('存放目标：' + labels[state.storageTarget], 'success');
    });
    $('#settingsStorageTarget').addEventListener('change', () => {
      state.storageTarget = $('#settingsStorageTarget').value;
      $('#storageTarget').value = state.storageTarget;
      post('saveSettings', settingsPayload());
      showToast('存放目标已保存', 'success');
    });
    $$('.stepper button').forEach((button) => button.addEventListener('click', () => {
      updateConcurrency(Number($('#settingsConcurrency').textContent) + Number(button.dataset.step));
    }));
    let searchTimer = null;
    $('#searchInput').addEventListener('input', (event) => {
      state.search = event.target.value;
      clearTimeout(searchTimer);
      searchTimer = setTimeout(() => requestCatalogPage(0), 240);
    });
    document.addEventListener('keydown', (event) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        $('#searchInput').focus();
      }
      if (event.key === 'Escape') {
        $$('.modal-backdrop:not(.hidden)').forEach(closeModal);
      }
    });
    $$('#statusFilter button').forEach((button) => button.addEventListener('click', () => {
      $$('#statusFilter button').forEach((item) => item.classList.remove('active'));
      button.classList.add('active');
      state.statusFilter = button.dataset.filter;
      requestCatalogPage(0);
    }));
    $('#pagePrev').addEventListener('click', () => requestCatalogPage(state.catalogMeta.page - 1));
    $('#pageNext').addEventListener('click', () => requestCatalogPage(state.catalogMeta.page + 1));
    $('#pageButtons').addEventListener('click', (event) => {
      const button = event.target.closest('[data-page]');
      if (button) requestCatalogPage(Number(button.dataset.page));
    });
    $('#chartGrid').addEventListener('click', (event) => {
      const download = event.target.closest('[data-download]');
      if (!download) return;
      requestSync(false, { chartId: download.dataset.download });
    });
    $('#siteLink').addEventListener('click', (event) => {
      event.preventDefault();
      post('openSite');
    });
    $('#cancelOperation').addEventListener('click', () => post('cancel'));
    $('#operationCancel').addEventListener('click', () => post('cancel'));
    $$('[data-close-modal]').forEach((button) => button.addEventListener('click', () => {
      closeModal(button.closest('.modal-backdrop'));
    }));
    $$('.modal-backdrop').forEach((modal) => modal.addEventListener('mousedown', (event) => {
      if (event.target === modal) closeModal(modal);
    }));
    $('#tourChoose').addEventListener('click', () => {
      post('completeTour');
      post('detectFolder');
      closeModal($('#tourModal'));
    });
    $('#tourLater').addEventListener('click', () => {
      post('completeTour');
      closeModal($('#tourModal'));
    });
    $$('.date-presets button').forEach((button) => button.addEventListener('click', () => {
      const today = new Date();
      const to = today.toISOString().slice(0, 10);
      let from = to;
      if (button.dataset.preset === '7') {
        const d = new Date(today); d.setDate(d.getDate() - 6); from = d.toISOString().slice(0, 10);
      } else if (button.dataset.preset === '30') {
        const d = new Date(today); d.setDate(d.getDate() - 29); from = d.toISOString().slice(0, 10);
      } else if (button.dataset.preset === 'month') {
        from = new Date(today.getFullYear(), today.getMonth(), 1).toISOString().slice(0, 10);
      }
      $('#modalDateFrom').value = from;
      $('#modalDateTo').value = to;
    }));
    $('#startDateSync').addEventListener('click', () => {
      const from = $('#modalDateFrom').value;
      const to = $('#modalDateTo').value;
      if (!from || !to) {
        showToast('请选择开始和结束日期', 'warning');
        return;
      }
      setDateFields(from, to);
      closeModal($('#dateModal'));
      requestSync($('#modalOnlyNew').checked, { dateFrom: from, dateTo: to });
    });
    $('#dropZone').addEventListener('click', () => post('selectZip'));
    $('#dropZone').addEventListener('keydown', (event) => {
      if (event.key === 'Enter' || event.key === ' ') post('selectZip');
    });
    $('#zipInput').addEventListener('change', (event) => {
      const file = event.target.files[0];
      if (!file) return;
      state.zipPath = file.path || file.name;
      $('#zipSelectedPath').textContent = state.zipPath;
      $('#startImport').disabled = false;
    });
    $('#startImport').addEventListener('click', () => {
      if (!state.zipPath) {
        showToast('请选择一个 ZIP 文件', 'warning');
        return;
      }
      closeModal($('#importModal'));
      post('importZip', { zipPath: state.zipPath });
    });
    $('#chooseGameFolder').addEventListener('click', () => post('chooseGameFolder'));
    $('#installGame').addEventListener('click', () => {
      if (!confirm('将从所选下载源下载完整 MajdataPlay 游戏包并解压到游戏目录，继续吗？')) return;
      post('installGame', { source: $('#gameSource').value });
      switchView('tasks');
    });
    $('#gameSource').addEventListener('change', () => {
      post('saveGameSource', { source: $('#gameSource').value });
      showToast('游戏下载源已切换', 'success');
    });
    $('#checkGameUpdate').addEventListener('click', () => post('checkGameUpdate'));
    $('#updateGame').addEventListener('click', () => post('updateGame'));
    $('#launchGame').addEventListener('click', () => {
      post('launchGame', { option: $('#launchOption').value });
    });
    $('#openGameFolder').addEventListener('click', () => post('openGameFolder'));
    $('#refreshSkins').addEventListener('click', () => post('loadSkins'));
    $('#saveGameSettings').addEventListener('click', () => {
      if (!state.gameSettings) return;
      const settings = JSON.parse(JSON.stringify(state.gameSettings));
      const source = $('#gameSettingsGrid input[data-online-source]')?.value;
      if (source && settings.Online) {
        settings.Online.ApiEndpoints ||= [];
        if (!settings.Online.ApiEndpoints.length) {
          settings.Online.ApiEndpoints.push({
            Name: 'MajdataNET',
            Url: source,
            Username: 'YourUsername',
            Password: 'YourPassword',
            AutoLogin: false
          });
        } else {
          settings.Online.ApiEndpoints[0].Url = source;
        }
      }
      $$('#gameSettingsGrid [data-setting-key]').forEach(input => {
        const section = input.dataset.settingSection;
        const key = input.dataset.settingKey;
        if (!settings[section]) return;
        settings[section][key] = input.type === 'checkbox'
          ? input.checked
          : input.type === 'number'
            ? (input.value === '' ? 0 : Number(input.value))
            : input.value;
      });
      if (source && settings.Online) {
        settings.Online.ApiEndpoints[0].Url = source;
      }
      post('saveGameSettings', { settings });
    });
    $('#loadAdvancedSettings').addEventListener('click', () => {
      if (!state.gameSettings) return;
      renderAdvancedSettings(state.gameSettings);
      $('#advancedSettings').open = true;
    });
    $('#advancedSettingsSearch').addEventListener('input', event => {
      filterAdvancedSettings(event.target.value);
    });
    $('#advancedSettingsSearch').addEventListener('keydown', event => {
      if (event.key === 'Escape') {
        event.target.value = '';
        filterAdvancedSettings('');
      }
    });
    $('#saveAdvancedSettings').addEventListener('click', () => {
      if (!state.gameSettings) return;
      try {
        const { settings, count } = collectAdvancedSettings();
        post('saveGameSettings', { settings });
        $('#advancedSettingsHint').textContent = `已提交 ${count} 个高级字段，正在保存…`;
      } catch (error) {
        $('#advancedSettingsHint').textContent = error.message;
        showToast(error.message, 'error');
      }
    });
    $('#skinGrid').addEventListener('click', (event) => {
      const button = event.target.closest('[data-skin-file]');
      if (!button) return;
      post('installSkin', {
        name: button.dataset.skinName,
        fileName: button.dataset.skinFile,
        size: Number(button.dataset.skinSize || 0)
      });
      switchView('tasks');
    });
    $('#installedSkins').addEventListener('click', (event) => {
      const button = event.target.closest('[data-apply-skin]');
      if (!button || button.disabled) return;
      if (!confirm(`将皮肤“${button.dataset.applySkin}”设为当前皮肤，继续吗？`)) return;
      post('applySkin', { name: button.dataset.applySkin });
    });
    $('#gameSettingsGrid').addEventListener('click', (event) => {
      const retry = event.target.closest('#testOnlineSources');
      if (retry) {
        startOnlineSourceTest();
        return;
      }

      const card = event.target.closest('[data-online-source-url]');
      if (!card) return;
      const input = $('#gameSettingsGrid [data-online-source]');
      if (input) input.value = card.dataset.onlineSourceUrl;
      $$('#gameSettingsGrid .online-source-card').forEach(item =>
        item.classList.toggle('selected', item === card));
      const note = $('#onlineSourceNote');
      if (note) note.textContent = '已选择此下载源，点击“保存设置”后写入游戏。';
    });
  }

  function bindHostMessages() {
    window.chrome.webview.addEventListener('message', (event) => {
      const { type, payload } = event.data || {};
      switch (type) {
        case 'folder':
          updateFolder(payload);
          break;
        case 'settings':
          state.concurrency = payload.concurrency || 6;
          state.includeVideo = payload.includeVideo !== false;
          state.autoCheck = payload.autoCheck === true;
          state.downloadMode = payload.downloadMode || 'auto';
          state.useMirrors = payload.useMirrors !== false;
          state.storageTarget = payload.storageTarget || 'both';
          updateNetworkModeUi();
          $('#concurrencyRange').value = state.concurrency;
          $('#concurrencyValue').textContent = state.concurrency;
          $('#settingsConcurrency').textContent = state.concurrency;
          $('#includeVideo').checked = state.includeVideo;
          $('#settingsVideo').checked = state.includeVideo;
          $('#settingsAutoCheck').checked = state.autoCheck;
          $('#downloadMode').value = state.downloadMode;
          $('#useMirrors').checked = state.useMirrors;
          $('#storageTarget').value = state.storageTarget;
          $('#settingsStorageTarget').value = state.storageTarget;
          if (payload.firstRun && !state.folder?.detected) openModal('tourModal');
          break;
        case 'autoCheckEnabled':
          state.autoCheck = true;
          $('#settingsAutoCheck').checked = true;
          break;
        case 'networkTesting':
          $('#networkNodeStatus').textContent = payload.message || '正在测速…';
          $('#networkCardText').textContent = payload.message || '正在测速…';
          break;
        case 'networkNode':
          $('#testNetwork').disabled = false;
          renderNetworkRoutes(payload.routes || [], payload.message || '测速完成');
          if (!$('#view-settings').classList.contains('active')) {
            $('#networkCardText').textContent = payload.fastest
              ? `启动测速：${payload.fastest} 最快，失败时自动切换。`
              : (payload.message || '自动优选节点已启用');
          }
          break;

        case 'stats':
          $('#statLocal').textContent = formatNumber(payload.folderCount);
          $('#statTracked').textContent = formatNumber(payload.trackedCount);
          $('#statLast').textContent = payload.lastSyncAt ? formatRelative(payload.lastSyncAt) : '—';
          break;
        case 'state':
          $('#footerStatus').textContent = state.indexing
            ? 'INDEXING'
            : payload.operationRunning ? 'WORKING' : 'READY';
          break;
        case 'catalog':
          requestCatalogPage(0);
          break;
        case 'catalogLoading':
          $('#catalogSummary').textContent = payload.message || '正在准备曲目索引…';
          break;
        case 'catalogUpdated':
          requestCatalogPage(state.catalogMeta.page);
          break;
        case 'catalogBackground':
          state.indexing = payload.running === true;
          $('#footerStatus').textContent = state.indexing
            ? 'INDEXING'
            : (state.busy ? $('#footerStatus').textContent : 'READY');
          $('#footerStatus').title = payload.message || '';
          $('#footerStatus').classList.toggle('indexing', state.indexing);
          if (!payload.running && payload.message) {
            $('#catalogSummary').textContent = payload.message;
          }
          break;
        case 'catalogPage':
          state.catalog = payload.items || [];
          state.catalogMeta = {
            total: payload.total || 0,
            filtered: payload.filtered || 0,
            downloaded: payload.downloaded || 0,
            newCount: payload.newCount || 0,
            page: payload.page || 0,
            pageCount: payload.pageCount || 1,
            pageSize: payload.pageSize || 24
          };
          $('#indexTime').textContent = payload.indexedAt
            ? `索引更新于 ${new Date(payload.indexedAt).toLocaleString('zh-CN')}`
            : '';
          renderCatalog();
          break;
        case 'gameStatus':
          renderGameStatus(payload);
          break;
        case 'skins':
          renderSkins(payload);
          break;
        case 'gameSettings':
          renderGameSettings(payload);
          break;
        case 'onlineSourceTesting':
          $('#gameSettingsGrid .online-test')?.setAttribute('disabled', '');
          {
            const note = $('#onlineSourceNote');
            if (note) note.textContent = payload.message || '正在测试连接延迟…';
          }
          break;
        case 'onlineSourceLatency':
          renderOnlineSourceLatency(payload);
          break;
        case 'gameSource':
          $('#gameSource').value = payload.source || 'cnb';
          break;
        case 'gameUpdates':
          state.gameUpdates = payload.count || 0;
          $('#gameUpdateText').textContent = state.gameUpdates
            ? `发现 ${state.gameUpdates} 个文件需要更新`
            : '游戏文件已是最新';
          $('#updateGame').disabled = state.gameUpdates === 0;
          break;
        case 'recent':
          renderRecent(payload);
          break;
        case 'operation':
          setOperation(payload);
          break;
        case 'busy':
          setBusy(payload.running);
          break;
        case 'task':
          renderTask(payload);
          break;
        case 'toast':
          showToast(payload.message, payload.kind);
          break;
        case 'zipPicked':
          state.zipPath = payload.path;
          $('#zipSelectedPath').textContent = payload.path;
          $('#startImport').disabled = false;
          openModal('importModal');
          break;
        case 'chartUpdated':
          requestCatalogPage(state.catalogMeta.page);
          break;
      }
    });
  }

  function init() {
    hydrateIcons();
    bootstrapEvents();
    bindHostMessages();
    setDateFields('', '');
    updateNetworkModeUi();
    post('ready');
  }

  init();
})();
