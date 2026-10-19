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
    dateFrom: '',
    dateTo: '',
    concurrency: 6,
    includeVideo: true,
    autoCheck: true,
    downloadMode: 'auto',
    useMirrors: true,
    storageTarget: 'both',
    busy: false,
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
      settings: ['MAJDATA · PREFERENCES', '偏好设置']
    };
    $('#viewEyebrow').textContent = titles[view][0];
    $('#viewTitle').textContent = titles[view][1];
    document.querySelector('.main').scrollTo({ top: 0, behavior: 'smooth' });
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
    if (state.busy) {
      showToast('已有任务正在运行', 'warning');
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

        case 'promptIndex':
          if (confirm(payload.message || '是否现在读取站点索引？')) {
            post('startIndex');
          } else {
            post('skipIndex');
          }
          break;
        case 'stats':
          $('#statLocal').textContent = formatNumber(payload.folderCount);
          $('#statTracked').textContent = formatNumber(payload.trackedCount);
          $('#statLast').textContent = payload.lastSyncAt ? formatRelative(payload.lastSyncAt) : '—';
          break;
        case 'state':
          $('#footerStatus').textContent = payload.operationRunning ? 'WORKING' : 'READY';
          break;
        case 'catalog':
          requestCatalogPage(0);
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
