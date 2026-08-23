(() => {
  'use strict';

  const T = {
    en: {
      library:'Library',install:'Install',updates:'Updates',logs:'Logs',settings:'Settings',restartRequired:'Unapplied changes',restartHint:'Keep making changes. RVCPM will apply everything together in one rebuild/restart.',restartDiscord:'Apply changes & restart Discord',managedData:'Managed data',customPlugins:'Custom plugins',librarySubtitle:'Install, update, configure and control Vencord userplugins from one place.',checkUpdates:'Check updates',addPlugins:'Add plugins',noPlugins:'No custom plugins yet',dropStart:'Drop .ts/.tsx files, a plugin folder or ZIP here — or install from GitHub.',chooseFiles:'Choose files',packageSources:'Package sources',installPlugins:'Install plugins',installSubtitle:'RVCPM validates plugins and stages them in AppData. Apply all changes once when you are ready.',dropHere:'Drop plugins here',dropTypes:'.ts · .tsx · plugin folders · .zip',files:'Files',folder:'Folder',orGithub:'or install from GitHub',githubHint:'Repository, /tree/ path or direct /blob/ plugin URL.',analyze:'Analyze',supportedFormats:'Supported Vencord plugin layouts',nativeNote:'is supported as a companion Node/Electron file inside a plugin folder. CSS and other imported assets are copied with the plugin. They are not independent plugin entry points.',securityNote:'Custom Vencord plugins execute code inside Discord. Install plugins only from sources you trust.',maintenance:'Maintenance',updatesSubtitle:'Check GitHub commits, local source changes and refresh Vencord before one combined rebuild.',updateAll:'Update all + Vencord',diagnostics:'Diagnostics',clear:'Clear',copy:'Copy',settingsSubtitle:'Machine-specific configuration is stored under Local AppData.',interface:'Interface',language:'Language',languageHint:'English is the default for new installations.',discordBranch:'Discord channel',branchHint:'Auto prefers a currently running installation, then Stable.',customLocation:'Custom Discord location',choose:'Choose',autoUpdateVencord:'Update Vencord before builds',autoUpdateVencordHint:'Fetches origin/main before installing, removing or updating plugins.',autoRestart:'Restart Discord after rebuild',autoRestartHint:'Only restarts Discord if it was running when the operation began.',enableAfterInstall:'Enable plugins after install',enableAfterInstallHint:'RVCPM stages the enabled state and safely applies it during restart.',devBuild:'Vencord Dev Build',devBuildHint:'Needed only for *.dev.ts(x) userplugins; normal users should leave this off.',paths:'Paths',managerData:'RVCPM data',vencordSource:'Managed Vencord source',vencordSettings:'Vencord settings.json',forceRebuild:'Update + rebuild + inject',forceRebuildHint:'Recreates src/userplugins from RVCPM’s library and injects a fresh build.',rebuild:'Rebuild',cancel:'Cancel',pluginSettings:'Plugin settings',save:'Save',close:'Close',remove:'Remove',removePlugin:'Remove plugin',removeQuestion:'Remove this plugin from the managed Vencord build?',removeSettings:'Also delete this plugin’s saved Vencord settings',source:'Source',openSource:'Open source',details:'Details',readme:'README',enabled:'Enabled',disabled:'Disabled',required:'Required',update:'Update',upToDate:'Up to date',updateAvailable:'Update available',localSourceChanged:'Local source changed',selectPlugins:'Select plugins to install',willReplace:'Already installed — will be replaced',selectAll:'Select all',installSelected:'Install selected',settingsUnavailable:'This setting uses a custom Discord component and cannot be safely reproduced outside Discord.',noConfigurableSettings:'No editable generic settings were detected.',customSettingsUi:'Custom settings UI',customSettingsUiBadge:'custom UI',customSettingsUiHint:'This plugin provides its own settings interface that runs inside Discord. RVCPM intentionally hides OptionType.CUSTOM storage because Vencord uses it for internal state such as caches, hashes, credentials and component data. Configure this part in Discord → User Settings → Vencord → Plugins+.',internalSettingsHidden:'Internal plugin storage is hidden by design.',runtimeCondition:'Discord applies an additional runtime condition to this setting.',installed:'Installed',github:'GitHub',local:'Local',zip:'ZIP',snapshot:'Drop snapshot',totalPlugins:'Managed plugins',availableUpdates:'Available updates',vencordBuild:'Vencord build',never:'Never',working:'Working…',operationFailed:'Operation failed',operationCancelled:'Operation cancelled',copied:'Copied to clipboard',saved:'Saved',restartStaged:'Change staged. Apply all changes when you are ready.',pluginTarget:'Target',settingsCount:'settings',description:'Description',pluginDescription:'Plugin description',installationSource:'Installation source',openGithub:'Open GitHub',noReadme:'No README was found for this source.',dragDetected:'Drop detected. RVCPM is analyzing the files…',stopped:'Stopped',managedSource:'Managed source',notPrepared:'Not prepared yet',stageToolchain:'Toolchain',stageDependencies:'Dependencies',stageBuild:'Build',stageDiscord:'Discord',stageInject:'Inject',stageUpdates:'Updates',stageDone:'Done'
    },
    ru: {
      library:'Библиотека',install:'Установка',updates:'Обновления',logs:'Логи',settings:'Настройки',restartRequired:'Есть неприменённые изменения',restartHint:'Можешь продолжать менять плагины. RVCPM применит всё разом одной сборкой и одним перезапуском.',restartDiscord:'Применить изменения и перезагрузить Discord',managedData:'Данные менеджера',customPlugins:'Кастомные плагины',librarySubtitle:'Установка, обновление, настройка и управление userplugins Vencord в одном месте.',checkUpdates:'Проверить обновления',addPlugins:'Добавить плагины',noPlugins:'Кастомных плагинов пока нет',dropStart:'Перетащи сюда .ts/.tsx, папку плагина или ZIP — либо установи проект с GitHub.',chooseFiles:'Выбрать файлы',packageSources:'Источники пакетов',installPlugins:'Установка плагинов',installSubtitle:'RVCPM проверяет плагины и откладывает изменения в AppData. Когда закончишь — примени всё одной сборкой.',dropHere:'Перетащи плагины сюда',dropTypes:'.ts · .tsx · папки плагинов · .zip',files:'Файлы',folder:'Папка',orGithub:'или установить с GitHub',githubHint:'Репозиторий, путь /tree/ или прямая ссылка /blob/ на плагин.',analyze:'Анализировать',supportedFormats:'Поддерживаемые структуры плагинов Vencord',nativeNote:'поддерживается как вспомогательный Node/Electron-файл внутри папки плагина. CSS и импортируемые ресурсы копируются вместе с плагином. Самостоятельными точками входа они не являются.',securityNote:'Кастомные плагины Vencord выполняют код внутри Discord. Устанавливай плагины только из источников, которым доверяешь.',maintenance:'Обслуживание',updatesSubtitle:'Проверка GitHub-коммитов, изменений локальных исходников и обновление Vencord перед единой пересборкой.',updateAll:'Обновить всё + Vencord',diagnostics:'Диагностика',clear:'Очистить',copy:'Копировать',settingsSubtitle:'Индивидуальная конфигурация этого ПК хранится в Local AppData.',interface:'Интерфейс',language:'Язык',languageHint:'Для новых установок по умолчанию используется английский.',discordBranch:'Канал Discord',branchHint:'Auto выбирает запущенную версию, затем Stable.',customLocation:'Своя папка Discord',choose:'Выбрать',autoUpdateVencord:'Обновлять Vencord перед сборкой',autoUpdateVencordHint:'Загружает origin/main перед установкой, удалением или обновлением плагинов.',autoRestart:'Перезапускать Discord после сборки',autoRestartHint:'Discord будет запущен снова только если он работал до операции.',enableAfterInstall:'Включать плагины после установки',enableAfterInstallHint:'RVCPM откладывает изменение и безопасно применяет его во время перезапуска.',devBuild:'Dev-сборка Vencord',devBuildHint:'Нужна только для *.dev.ts(x); обычно оставляй выключенной.',paths:'Пути',managerData:'Данные RVCPM',vencordSource:'Исходники Vencord',vencordSettings:'Vencord settings.json',forceRebuild:'Обновить + собрать + внедрить',forceRebuildHint:'Заново создаёт src/userplugins из библиотеки RVCPM и внедряет свежую сборку.',rebuild:'Пересобрать',cancel:'Отмена',pluginSettings:'Настройки плагина',save:'Сохранить',close:'Закрыть',remove:'Удалить',removePlugin:'Удаление плагина',removeQuestion:'Удалить этот плагин из управляемой сборки Vencord?',removeSettings:'Также удалить сохранённые настройки этого плагина из Vencord',source:'Источник',openSource:'Открыть исходники',details:'Подробнее',readme:'README',enabled:'Включён',disabled:'Выключен',required:'Обязательный',update:'Обновить',upToDate:'Актуально',updateAvailable:'Доступно обновление',localSourceChanged:'Локальный исходник изменён',selectPlugins:'Выбери плагины для установки',willReplace:'Уже установлен — будет заменён',selectAll:'Выбрать все',installSelected:'Установить выбранные',settingsUnavailable:'Эта настройка использует кастомный компонент Discord и не может безопасно редактироваться вне Discord.',noConfigurableSettings:'Редактируемых стандартных настроек не обнаружено.',customSettingsUi:'Собственный интерфейс настроек',customSettingsUiBadge:'свой UI',customSettingsUiHint:'Этот плагин использует собственный интерфейс настроек, который работает внутри Discord. RVCPM намеренно скрывает хранилище OptionType.CUSTOM: Vencord использует его для внутреннего состояния, кэшей, хешей, учётных данных и данных компонентов. Настраивай эту часть в Discord → Настройки пользователя → Vencord → Plugins+.',internalSettingsHidden:'Внутреннее хранилище плагина скрыто специально.',runtimeCondition:'В Discord к этой настройке дополнительно применяется динамическое условие.',installed:'Установлен',github:'GitHub',local:'Локальный',zip:'ZIP',snapshot:'Снимок Drag & Drop',totalPlugins:'Плагинов',availableUpdates:'Обновлений',vencordBuild:'Сборка Vencord',never:'Никогда',working:'Выполняется…',operationFailed:'Ошибка операции',operationCancelled:'Операция отменена',copied:'Скопировано',saved:'Сохранено',restartStaged:'Изменение отложено. Примени все изменения, когда закончишь.',pluginTarget:'Цель',settingsCount:'настроек',description:'Описание',pluginDescription:'Описание плагина',installationSource:'Источник установки',openGithub:'Открыть GitHub',noReadme:'README для этого источника не найден.',dragDetected:'Файлы получены. RVCPM анализирует их…',stopped:'Остановлен',managedSource:'Управляемые исходники',notPrepared:'Ещё не подготовлен',stageToolchain:'Инструменты',stageDependencies:'Зависимости',stageBuild:'Сборка',stageDiscord:'Discord',stageInject:'Инжект',stageUpdates:'Обновления',stageDone:'Готово'
    },
    uk: {
      library:'Бібліотека',install:'Встановлення',updates:'Оновлення',logs:'Логи',settings:'Налаштування',restartRequired:'Є незастосовані зміни',restartHint:'Можеш продовжувати змінювати плагіни. RVCPM застосує все разом однією збіркою та одним перезапуском.',restartDiscord:'Застосувати зміни й перезапустити Discord',managedData:'Дані менеджера',customPlugins:'Кастомні плагіни',librarySubtitle:'Встановлення, оновлення, налаштування та керування userplugins Vencord в одному місці.',checkUpdates:'Перевірити оновлення',addPlugins:'Додати плагіни',noPlugins:'Кастомних плагінів ще немає',dropStart:'Перетягни сюди .ts/.tsx, папку плагіна або ZIP — чи встанови проєкт з GitHub.',chooseFiles:'Вибрати файли',packageSources:'Джерела пакетів',installPlugins:'Встановлення плагінів',installSubtitle:'RVCPM перевіряє плагіни та відкладає зміни в AppData. Коли закінчиш — застосуй усе однією збіркою.',dropHere:'Перетягни плагіни сюди',dropTypes:'.ts · .tsx · папки плагінів · .zip',files:'Файли',folder:'Папка',orGithub:'або встановити з GitHub',githubHint:'Репозиторій, шлях /tree/ або пряме посилання /blob/ на плагін.',analyze:'Аналізувати',supportedFormats:'Підтримувані структури плагінів Vencord',nativeNote:'підтримується як допоміжний Node/Electron-файл усередині папки плагіна. CSS та імпортовані ресурси копіюються разом із плагіном. Окремими точками входу вони не є.',securityNote:'Кастомні плагіни Vencord виконують код усередині Discord. Встановлюй плагіни лише з джерел, яким довіряєш.',maintenance:'Обслуговування',updatesSubtitle:'Перевірка GitHub-комітів, змін локальних вихідних файлів і оновлення Vencord перед єдиною перебудовою.',updateAll:'Оновити все + Vencord',diagnostics:'Діагностика',clear:'Очистити',copy:'Копіювати',settingsSubtitle:'Індивідуальна конфігурація цього ПК зберігається в Local AppData.',interface:'Інтерфейс',language:'Мова',languageHint:'Для нових інсталяцій типовою мовою є англійська.',discordBranch:'Канал Discord',branchHint:'Auto обирає запущену версію, потім Stable.',customLocation:'Власна папка Discord',choose:'Вибрати',autoUpdateVencord:'Оновлювати Vencord перед збіркою',autoUpdateVencordHint:'Завантажує origin/main перед встановленням, видаленням або оновленням плагінів.',autoRestart:'Перезапускати Discord після збірки',autoRestartHint:'Discord буде запущено знову лише якщо він працював до операції.',enableAfterInstall:'Увімкнути плагіни після встановлення',enableAfterInstallHint:'RVCPM відкладає зміну й безпечно застосовує її під час перезапуску.',devBuild:'Dev-збірка Vencord',devBuildHint:'Потрібна лише для *.dev.ts(x); зазвичай залишай вимкненою.',paths:'Шляхи',managerData:'Дані RVCPM',vencordSource:'Вихідні файли Vencord',vencordSettings:'Vencord settings.json',forceRebuild:'Оновити + зібрати + інжектнути',forceRebuildHint:'Наново створює src/userplugins із бібліотеки RVCPM та інжектить свіжу збірку.',rebuild:'Перезібрати',cancel:'Скасувати',pluginSettings:'Налаштування плагіна',save:'Зберегти',close:'Закрити',remove:'Видалити',removePlugin:'Видалення плагіна',removeQuestion:'Видалити цей плагін з керованої збірки Vencord?',removeSettings:'Також видалити збережені налаштування цього плагіна з Vencord',source:'Джерело',openSource:'Відкрити вихідні файли',details:'Докладніше',readme:'README',enabled:'Увімкнено',disabled:'Вимкнено',required:'Обов’язковий',update:'Оновити',upToDate:'Актуально',updateAvailable:'Доступне оновлення',localSourceChanged:'Локальне джерело змінено',selectPlugins:'Обери плагіни для встановлення',willReplace:'Вже встановлено — буде замінено',selectAll:'Вибрати всі',installSelected:'Встановити вибрані',settingsUnavailable:'Це налаштування використовує кастомний компонент Discord і не може безпечно редагуватися поза Discord.',noConfigurableSettings:'Редагованих стандартних налаштувань не виявлено.',customSettingsUi:'Власний інтерфейс налаштувань',customSettingsUiBadge:'власний UI',customSettingsUiHint:'Цей плагін використовує власний інтерфейс налаштувань, який працює всередині Discord. RVCPM навмисно приховує сховище OptionType.CUSTOM: Vencord використовує його для внутрішнього стану, кешів, хешів, облікових даних і даних компонентів. Налаштовуй цю частину в Discord → Налаштування користувача → Vencord → Plugins+.',internalSettingsHidden:'Внутрішнє сховище плагіна навмисно приховане.',runtimeCondition:'У Discord до цього налаштування додатково застосовується динамічна умова.',installed:'Встановлено',github:'GitHub',local:'Локальний',zip:'ZIP',snapshot:'Знімок Drag & Drop',totalPlugins:'Плагінів',availableUpdates:'Оновлень',vencordBuild:'Збірка Vencord',never:'Ніколи',working:'Виконується…',operationFailed:'Помилка операції',operationCancelled:'Операцію скасовано',copied:'Скопійовано',saved:'Збережено',restartStaged:'Зміну відкладено. Застосуй усі зміни, коли закінчиш.',pluginTarget:'Ціль',settingsCount:'налаштувань',description:'Опис',pluginDescription:'Опис плагіна',installationSource:'Джерело встановлення',openGithub:'Відкрити GitHub',noReadme:'README для цього джерела не знайдено.',dragDetected:'Файли отримано. RVCPM аналізує їх…',stopped:'Зупинено',managedSource:'Керовані вихідні файли',notPrepared:'Ще не підготовлено',stageToolchain:'Інструменти',stageDependencies:'Залежності',stageBuild:'Збірка',stageDiscord:'Discord',stageInject:'Інжект',stageUpdates:'Оновлення',stageDone:'Готово'
    }
  };

  let state = { language:'en', plugins:[] };
  let language = 'en';
  let operationActive = false;
  let operationLog = '';
  const pending = new Map();
  let rpcSeq = 0;

  const $ = s => document.querySelector(s);
  const $$ = s => [...document.querySelectorAll(s)];
  const tr = key => (T[language] && T[language][key]) || T.en[key] || key;
  const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));

  function rpc(action,payload={}){
    const id = `r${Date.now()}_${++rpcSeq}`;
    return new Promise((resolve,reject)=>{
      pending.set(id,{resolve,reject});
      window.chrome.webview.postMessage({type:'rpc',id,action,payload});
      setTimeout(()=>{if(pending.has(id)){pending.delete(id);reject(new Error('RVCPM UI request timed out.'));}},30*60*1000);
    });
  }
  function fire(action,payload={}){ window.chrome.webview.postMessage({type:'rpc',id:`f${++rpcSeq}`,action,payload}); }

  window.chrome.webview.addEventListener('message', e => {
    let m=e.data; if(typeof m==='string'){try{m=JSON.parse(m)}catch{return}}
    if(!m)return;
    if(m.type==='rpcResult'){
      const p=pending.get(m.id); if(!p)return; pending.delete(m.id);
      m.ok?p.resolve(m.data):p.reject(new Error(m.error||'Unknown RVCPM error'));
      return;
    }
    if(m.type==='event') onNativeEvent(m.name,m.data);
  });

  function onNativeEvent(name,data){
    if(name==='stateChanged'){state=data||state; renderState(); return;}
    if(name==='candidateBatch'){openCandidateModal(data); return;}
    if(name==='operationStarted'){
      operationActive=true; operationLog=''; $('#opLog').textContent=''; $('#operationOverlay').classList.remove('hidden');
      $('#opTitle').textContent=tr('working'); $('#opMessage').textContent=data?.name||''; setProgress(-1); return;
    }
    if(name==='progress'){
      $('#opMessage').textContent=data?.message||''; if(data?.stage) $('#opTitle').textContent=prettyStage(data.stage); setProgress(data?.percent??-1);
      $('#cancelOperation').style.display=data?.canCancel===false?'none':''; return;
    }
    if(name==='log'){
      const line=(data?.line||'')+'\n'; operationLog+=line; const op=$('#opLog'); op.textContent=operationLog.slice(-30000); op.scrollTop=op.scrollHeight;
      const lv=$('#logView'); if(lv&&$('#page-logs').classList.contains('active')){lv.textContent+=line;lv.scrollTop=lv.scrollHeight;} return;
    }
    if(name==='logCleared'){ const lv=$('#logView');if(lv)lv.textContent='';return; }
    if(name==='operationFinished'){
      operationActive=false;
      if(data?.ok){setTimeout(()=>$('#operationOverlay').classList.add('hidden'),450);}
      else {$('#operationOverlay').classList.add('hidden');toast(data?.cancelled?tr('operationCancelled'):(data?.error||tr('operationFailed')),'error');}
      return;
    }
    if(name==='toast') toast(data?.message||'',data?.kind||'info');
  }

  function prettyStage(s){return ({toolchain:tr('stageToolchain'),github:'GitHub',plugins:'Plugins+',dependencies:tr('stageDependencies'),build:tr('stageBuild'),discord:tr('stageDiscord'),inject:tr('stageInject'),updates:tr('stageUpdates'),done:tr('stageDone')})[s]||s;}
  function setProgress(p){const f=$('#progressFill'),txt=$('#opPercent');if(p==null||p<0){f.classList.add('indeterminate');txt.textContent='…';}else{f.classList.remove('indeterminate');f.style.width=`${Math.max(0,Math.min(100,p))}%`;txt.textContent=`${p}%`;}}

  function applyTranslations(){
    document.documentElement.lang=language==='uk'?'uk':language==='ru'?'ru':'en';
    $$('[data-i18n]').forEach(el=>{const k=el.dataset.i18n;if(tr(k))el.textContent=tr(k)});
  }

  function renderState(){
    language=['en','ru','uk'].includes(state.language)?state.language:'en'; applyTranslations();
    $('#pluginCount').textContent=state.plugins?.length||0;
    $('#restartBanner').classList.toggle('hidden',!state.pendingChanges);
    $('#discordDot').classList.toggle('on',!!state.discordRunning); $('#discordState').textContent=state.discordRunning?state.discordStatus:tr('stopped');
    $('#vencordState').textContent=state.vencordInstalledByManager?(state.vencordVersion||tr('managedSource')):tr('notPrepared');
    $('#pluginsPlusState').textContent=(state.plugins?.length||0)>0?'Plugins+':'Plugins';
    $('#titleStatus').innerHTML=`<span class="status-pill"><i class="mini-dot ${state.discordRunning?'on':''}"></i>Discord</span><span class="status-pill">Vencord ${esc(state.vencordVersion||'—')}</span>`;
    $('#dataPathShort').textContent=shortPath(state.dataPath||'');
    $('#emptyLibrary').classList.toggle('hidden',(state.plugins?.length||0)!==0);
    renderPlugins(); renderUpdates(); renderSupported(); renderSettingsValues();
    $('#updateDot').classList.toggle('hidden',!(state.plugins||[]).some(p=>p.updateAvailable));
  }

  function shortPath(p){if(!p)return'';const local=(p.match(/\\AppData\\Local\\(.+)$/i)||[])[1];return local?`%LOCALAPPDATA%\\${local}`:p;}
  function initials(name){return (name||'?').split(/\s+/).map(x=>x[0]).join('').slice(0,2).toUpperCase();}
  function sourceLabel(p){return p.sourceKind==='GitHub'?'GitHub':p.sourceKind==='Zip'?'ZIP':p.sourceKind==='DropSnapshot'?tr('snapshot'):tr('local');}

  function renderPlugins(){
    const grid=$('#pluginGrid');grid.innerHTML='';
    (state.plugins||[]).forEach((p,i)=>{
      const card=document.createElement('article');card.className='plugin-card';card.style.animationDelay=`${Math.min(i*35,250)}ms`;
      card.innerHTML=`${p.updateAvailable?`<div class="update-badge">${esc(tr('updateAvailable'))}</div>`:''}
        <div class="plugin-top"><div class="plugin-avatar">${esc(initials(p.name))}</div><div class="plugin-ident"><div class="plugin-name-row"><span class="plugin-name">${esc(p.name)}</span><span class="source-badge">${sourceLabel(p)}</span></div><div class="version">${esc(p.version||'—')} · ${esc(p.target||'desktop/default')}</div></div><input class="toggle plugin-toggle" type="checkbox" ${p.enabled?'checked':''} ${p.required?'disabled':''} aria-label="Enabled" /></div>
        <div class="plugin-desc">${esc(p.description||p.pluginDescription||'—')}</div>
        <div class="plugin-footer">${p.required?`<span class="plugin-meta-pill">${esc(tr('required'))}</span>`:''}${p.settingsCount?`<span class="plugin-meta-pill">${p.settingsCount} ${esc(tr('settingsCount'))}</span>`:''}${p.customSettingsUi?`<span class="plugin-meta-pill custom-ui-pill">${esc(tr('customSettingsUiBadge'))}</span>`:''}${p.dependencies?.length?`<span class="plugin-meta-pill">deps ${p.dependencies.length}</span>`:''}<span class="spacer"></span>${p.hasSettings?`<button class="icon-btn settings-btn" title="${esc(tr('pluginSettings'))}">⚙</button>`:''}<button class="icon-btn details-btn" title="${esc(tr('details'))}">…</button></div>`;
      card.querySelector('.plugin-toggle').addEventListener('change',async e=>{try{await rpc('togglePlugin',{pluginId:p.id,enabled:e.target.checked});toast(tr('restartStaged'),'info')}catch(err){e.target.checked=!e.target.checked;toast(err.message,'error')}});
      card.querySelector('.details-btn').addEventListener('click',()=>openDetails(p));
      card.querySelector('.settings-btn')?.addEventListener('click',()=>openSettings(p));
      grid.appendChild(card);
    });
  }

  function renderUpdates(){
    const plugins=state.plugins||[], updates=plugins.filter(p=>p.updateAvailable);
    $('#updateSummary').innerHTML=`<div class="summary-card"><small>${esc(tr('totalPlugins'))}</small><strong>${plugins.length}</strong></div><div class="summary-card"><small>${esc(tr('availableUpdates'))}</small><strong>${updates.length}</strong></div><div class="summary-card"><small>${esc(tr('vencordBuild'))}</small><strong style="font-size:14px;margin-top:9px">${esc(state.vencordVersion||'—')} ${state.vencordCommit?`(${esc(state.vencordCommit)})`:''}</strong></div>`;
    const list=$('#updateList');list.innerHTML='';
    if(!plugins.length){list.innerHTML=`<div class="empty-state" style="min-height:250px"><h2>${esc(tr('noPlugins'))}</h2></div>`;return;}
    plugins.forEach(p=>{
      const row=document.createElement('div');row.className='update-row';
      row.innerHTML=`<div class="plugin-avatar">${esc(initials(p.name))}</div><div class="update-info"><strong>${esc(p.name)}</strong><span class="${p.updateAvailable?'has-update':'up-to-date'}">${esc(p.updateAvailable?tr('updateAvailable'):tr('upToDate'))} · ${sourceLabel(p)}</span></div><button class="btn ${p.updateAvailable?'primary':'ghost'} small" ${p.updateAvailable?'':'disabled'}>${esc(tr('update'))}</button>`;
      row.querySelector('button').addEventListener('click',()=>withToast(()=>rpc('updatePlugin',{pluginId:p.id})));
      list.appendChild(row);
    });
  }

  function renderSupported(){
    const root=$('#supportedList');root.innerHTML='';(state.supported||[]).forEach(x=>{const d=document.createElement('div');d.className='support-item';d.textContent=x;root.appendChild(d)});
  }

  function renderSettingsValues(){
    $('#languageSelect').value=language; $('#branchSelect').value=state.discordBranch||'auto';
    $('#customLocationText').textContent=state.customDiscordLocation||'—';
    $('#autoUpdateVencord').checked=!!state.autoUpdateVencord;$('#autoRestart').checked=!!state.autoRestartAfterInstall;$('#enableAfterInstall').checked=!!state.enableAfterInstall;$('#devBuild').checked=!!state.devBuild;
    $('#settingsDataPath').textContent=state.dataPath||'';$('#settingsVencordPath').textContent=state.vencordPath||'';$('#settingsJsonPath').textContent=state.settingsPath||'';
  }

  function openCandidateModal(batch){
    const list=(batch?.candidates||[]).map(c=>`<label class="candidate"><input type="checkbox" class="candidate-check" value="${esc(c.id)}" checked/><div class="candidate-main"><div class="candidate-name">${esc(c.name)} <span class="source-badge">${esc(c.target)}</span>${c.alreadyInstalled?` <span class="replace-badge">${esc(tr('willReplace'))}</span>`:''}</div><div class="candidate-path">${esc(c.relativePath||batch.source||'')}</div>${c.description?`<div class="candidate-desc">${esc(c.description)}</div>`:''}<div class="modal-meta">${c.required?`<span class="plugin-meta-pill">${esc(tr('required'))}</span>`:''}${c.settingsCount?`<span class="plugin-meta-pill">${c.settingsCount} ${esc(tr('settingsCount'))}</span>`:''}${c.customSettingsUi?`<span class="plugin-meta-pill custom-ui-pill">${esc(tr('customSettingsUiBadge'))}</span>`:''}${c.version?`<span class="plugin-meta-pill">${esc(c.version)}</span>`:''}</div>${(c.warnings||[]).map(w=>`<div class="warning-line">⚠ ${esc(w)}</div>`).join('')}</div></label>`).join('');
    openModal(`<div class="modal-head"><div><div class="eyebrow">${esc(batch.sourceKind||'Plugin source')}</div><h2>${esc(tr('selectPlugins'))}</h2><p>${esc(batch.source||'')}</p></div><button class="modal-close" data-close>×</button></div><div class="modal-body"><label class="remove-check"><input type="checkbox" id="selectAllCandidates" checked/> ${esc(tr('selectAll'))}</label><div class="candidate-list" style="margin-top:10px">${list}</div></div><div class="modal-foot"><button class="btn ghost" data-close>${esc(tr('cancel'))}</button><button class="btn primary" id="installCandidates">${esc(tr('installSelected'))}</button></div>`);
    $('#selectAllCandidates').addEventListener('change',e=>$$('.candidate-check').forEach(x=>x.checked=e.target.checked));
    $('#installCandidates').addEventListener('click',async()=>{
      const ids=$$('.candidate-check:checked').map(x=>x.value);if(!ids.length){toast(tr('selectPlugins'),'error');return;}
      closeModal();try{await rpc('installCandidates',{batchId:batch.batchId,candidateIds:ids});toast(tr('restartStaged'),'info')}catch(err){toast(err.message,'error')}
    });
  }

  async function openSettings(p){
    try{
      const data=await rpc('getPluginSettings',{pluginId:p.id}), settings=data.settings||[], hasCustomUi=!!data.hasCustomSettingsUi;
      let body=settings.map(s=>settingHtml(s)).join('');
      if(hasCustomUi){
        body+=`<div class="custom-settings-card"><div class="custom-settings-icon">◈</div><div><div class="plugin-setting-title">${esc(tr('customSettingsUi'))}</div><div class="plugin-setting-desc">${esc(tr('customSettingsUiHint'))}</div></div></div>`;
      }
      if(!settings.length&&!hasCustomUi)body=`<div class="unsupported-setting">${esc(tr('noConfigurableSettings'))}</div>`;
      const footer=`<button class="btn ghost" data-close>${esc(settings.length?tr('cancel'):tr('close'))}</button>${settings.length?`<button class="btn primary" id="savePluginSettings">${esc(tr('save'))}</button>`:''}`;
      openModal(`<div class="modal-head"><div><div class="eyebrow">${esc(p.name)}</div><h2>${esc(tr('pluginSettings'))}</h2><p>${esc(p.pluginDescription||p.description||'')}</p></div><button class="modal-close" data-close>×</button></div><div class="modal-body"><div class="settings-form">${body}</div></div><div class="modal-foot">${footer}</div>`);
      $$('.slider-control').forEach(el=>el.addEventListener('input',()=>{el.parentElement.querySelector('.slider-value').textContent=el.value}));
      $('#savePluginSettings')?.addEventListener('click',async()=>{
        const values={};
        settings.forEach(s=>{
          if(s.unsupported||s.disabled)return;const el=document.querySelector(`[data-setting-key="${cssEscape(s.key)}"]`);if(!el)return;
          if(s.type==='Boolean')values[s.key]=el.checked;
          else if(s.type==='Number'||s.type==='Slider')values[s.key]=Number(el.value);
          else if(s.type==='Select'){const opt=s.options[Number(el.value)];if(opt)values[s.key]=opt.Value??opt.value;}
          else values[s.key]=el.value;
        });
        try{await rpc('savePluginSettings',{pluginId:p.id,values});closeModal();toast(tr('restartStaged'),'info')}catch(err){toast(err.message,'error')}
      });
    }catch(err){toast(err.message,'error')}
  }

  function settingHtml(s){
    const title=esc(s.displayName||s.key),desc=esc(s.description||''),key=esc(s.key),disabled=s.disabled?'disabled':'';
    if(s.unsupported)return '';
    let control='';
    const ph=s.placeholder?` placeholder="${esc(s.placeholder)}"`:'';
    if(s.type==='Boolean')control=`<input data-setting-key="${key}" class="toggle" type="checkbox" ${s.value===true?'checked':''} ${disabled}/>`;
    else if(s.type==='Number')control=`<input data-setting-key="${key}" class="text-input" type="number" value="${esc(s.value??s.defaultValue??0)}"${ph} ${disabled}/>`;
    else if(s.type==='Select')control=`<select data-setting-key="${key}" class="select" ${disabled}>${(s.options||[]).map((o,i)=>`<option value="${i}" ${deepEqual((o.Value??o.value),s.value)?'selected':''}>${esc(o.Label??o.label??String(o.Value??o.value))}</option>`).join('')}</select>`;
    else if(s.type==='Slider'){
      const marks=s.markers||[],val=s.value??s.defaultValue??(marks.length?marks[0]:0);
      if(s.stickToMarkers&&marks.length)control=`<select data-setting-key="${key}" class="select" ${disabled}>${marks.map(v=>`<option value="${esc(v)}" ${Number(v)===Number(val)?'selected':''}>${esc(v)}</option>`).join('')}</select>`;
      else{const min=marks.length?Math.min(...marks):0,max=marks.length?Math.max(...marks):100;control=`<div class="slider-wrap"><input data-setting-key="${key}" class="slider-control" type="range" min="${min}" max="${max}" step="${marks.length>1?Math.max(.01,(max-min)/100):1}" value="${esc(val)}" ${disabled}/><span class="slider-value">${esc(val)}</span></div>`;}
    }
    else if(s.multiline)control=`<textarea data-setting-key="${key}"${ph} ${disabled}>${esc(s.value??s.defaultValue??'')}</textarea>`;
    else control=`<input data-setting-key="${key}" class="text-input" value="${esc(s.value??s.defaultValue??'')}"${ph} ${disabled}/>`;
    const conditional=(s.conditionalVisibility||s.conditionalDisabled)?`<div class="setting-runtime-note">${esc(tr('runtimeCondition'))}</div>`:'';
    return `<div class="plugin-setting"><div class="plugin-setting-head"><div><div class="plugin-setting-title">${title}</div><div class="plugin-setting-desc">${desc}</div>${conditional}</div>${s.type==='Boolean'?control:''}</div>${s.type==='Boolean'?'':`<div class="plugin-setting-control">${control}</div>`}</div>`;
  }

  function openDetails(p){
    const readme=p.readme?markdown(p.readme):`<span style="color:var(--muted)">${esc(tr('noReadme'))}</span>`;
    openModal(`<div class="modal-head"><div><div class="eyebrow">${esc(sourceLabel(p))}</div><h2>${esc(p.name)}</h2><p>${esc(p.version||'')} · ${esc(p.target||'')}</p></div><button class="modal-close" data-close>×</button></div><div class="modal-body"><p class="details-desc">${esc(p.description||p.pluginDescription||'')}</p>${p.pluginDescription&&p.pluginDescription!==p.description?`<div class="notice"><b>${esc(tr('pluginDescription'))}</b><span>${esc(p.pluginDescription)}</span></div>`:''}<div class="modal-meta"><span class="plugin-meta-pill">${esc(tr(p.enabled?'enabled':'disabled'))}</span>${p.settingsCount?`<span class="plugin-meta-pill">${p.settingsCount} ${esc(tr('settingsCount'))}</span>`:''}${p.customSettingsUi?`<span class="plugin-meta-pill custom-ui-pill">${esc(tr('customSettingsUiBadge'))}</span>`:''}<span class="plugin-meta-pill">${esc(sourceLabel(p))}</span></div><h3 style="margin-top:16px;font-size:12px">${esc(tr('readme'))}</h3><div class="readme-box">${readme}</div></div><div class="modal-foot"><button class="btn danger ghost" id="removePluginBtn">${esc(tr('remove'))}</button><span style="flex:1"></span><button class="btn ghost" id="openSourceBtn">${esc(tr('openSource'))}</button>${p.githubUrl?`<button class="btn ghost" id="openGithubBtn">GitHub ↗</button>`:''}${p.hasSettings?`<button class="btn primary" id="detailsSettingsBtn">${esc(tr('pluginSettings'))}</button>`:''}</div>`);
    $('#openSourceBtn').addEventListener('click',()=>withToast(()=>rpc('openPluginSource',{pluginId:p.id}),false));
    $('#openGithubBtn')?.addEventListener('click',()=>fire('openExternal',{url:p.githubUrl}));
    $('#detailsSettingsBtn')?.addEventListener('click',()=>{closeModal();openSettings(p)});
    $('#removePluginBtn').addEventListener('click',()=>openRemoveModal(p));
  }

  function openRemoveModal(p){
    openModal(`<div class="modal-head"><div><div class="eyebrow">${esc(p.name)}</div><h2>${esc(tr('removePlugin'))}</h2><p>${esc(tr('removeQuestion'))}</p></div><button class="modal-close" data-close>×</button></div><div class="modal-body"><label class="remove-check"><input type="checkbox" id="removeSettingsCheck"/> ${esc(tr('removeSettings'))}</label></div><div class="modal-foot"><button class="btn ghost" data-close>${esc(tr('cancel'))}</button><button class="btn danger ghost" id="confirmRemove">${esc(tr('remove'))}</button></div>`);
    $('#confirmRemove').addEventListener('click',async()=>{const removeSettings=$('#removeSettingsCheck').checked;closeModal();try{await rpc('removePlugin',{pluginId:p.id,removeSettings});toast(tr('restartStaged'),'info')}catch(err){toast(err.message,'error')}});
  }

  function openModal(html){$('#modalRoot').innerHTML=`<div class="modal-backdrop"><div class="modal">${html}</div></div>`;$$('[data-close]').forEach(x=>x.addEventListener('click',closeModal));$('.modal-backdrop')?.addEventListener('mousedown',e=>{if(e.target.classList.contains('modal-backdrop'))closeModal()});}
  function closeModal(){$('#modalRoot').innerHTML='';}

  function markdown(src){
    let s=esc(src.slice(0,50000));
    s=s.replace(/^### (.+)$/gm,'<h3>$1</h3>').replace(/^## (.+)$/gm,'<h2>$1</h2>').replace(/^# (.+)$/gm,'<h1>$1</h1>');
    s=s.replace(/`([^`]+)`/g,'<code>$1</code>').replace(/\*\*([^*]+)\*\*/g,'<strong>$1</strong>').replace(/\n/g,'<br>');return s;
  }
  function deepEqual(a,b){return JSON.stringify(a)===JSON.stringify(b)}
  function cssEscape(s){return String(s).replace(/["\\]/g,'\\$&')}

  let lastToastMessage='',lastToastKind='',lastToastAt=0;
  function toast(message,kind='info'){message=String(message??'');const now=Date.now();if(message===lastToastMessage&&kind===lastToastKind&&now-lastToastAt<1500)return;lastToastMessage=message;lastToastKind=kind;lastToastAt=now;const d=document.createElement('div');d.className=`toast ${kind}`;d.innerHTML=`<span>${esc(message)}</span>`;$('#toastRoot').appendChild(d);setTimeout(()=>{d.style.opacity='0';d.style.transform='translateX(12px)';setTimeout(()=>d.remove(),220)},4200)}
  async function withToast(fn,success=true){try{await fn();if(success)toast(tr('saved'),'success')}catch(err){toast(err.message,'error')}}

  function gotoPage(page){$$('.page').forEach(p=>p.classList.toggle('active',p.id===`page-${page}`));$$('.nav-item').forEach(n=>n.classList.toggle('active',n.dataset.page===page));if(page==='logs')loadLogs();}
  async function loadLogs(){try{const r=await rpc('getLogs');$('#logView').textContent=r.text||'';$('#logView').scrollTop=$('#logView').scrollHeight}catch(e){}}
  async function saveAppSettings(){
    try{const s=await rpc('saveAppSettings',{language:$('#languageSelect').value,discordBranch:$('#branchSelect').value,customDiscordLocation:state.customDiscordLocation||'',autoUpdateVencord:$('#autoUpdateVencord').checked,autoRestartAfterInstall:$('#autoRestart').checked,enableAfterInstall:$('#enableAfterInstall').checked,devBuild:$('#devBuild').checked});state=s;renderState();}catch(err){toast(err.message,'error')}
  }

  $$('.nav-item').forEach(n=>n.addEventListener('click',()=>gotoPage(n.dataset.page)));
  $$('[data-goto]').forEach(n=>n.addEventListener('click',()=>gotoPage(n.dataset.goto)));
  $$('[data-window]').forEach(b=>b.addEventListener('click',()=>fire(b.dataset.window)));
  $('#titleDrag').addEventListener('mousedown',e=>{if(e.button===0)fire('beginDrag')});
  $('#restartDiscordBtn').addEventListener('click',()=>withToast(()=>rpc('applyPendingChanges'),false));
  $('#dataPathBtn').addEventListener('click',()=>fire('openDataFolder'));$('#openDataFolder').addEventListener('click',()=>fire('openDataFolder'));
  $('#pickFiles').addEventListener('click',()=>withToast(()=>rpc('browseFiles'),false));$('#emptyFiles').addEventListener('click',()=>withToast(()=>rpc('browseFiles'),false));
  $('#pickFolder').addEventListener('click',()=>withToast(()=>rpc('browseFolder'),false));
  $('#emptyGithub').addEventListener('click',()=>{gotoPage('install');setTimeout(()=>$('#githubUrl').focus(),100)});
  $('#githubAnalyze').addEventListener('click',()=>{const url=$('#githubUrl').value.trim();if(!url)return;withToast(()=>rpc('analyzeGithub',{url}),false)});
  $('#githubUrl').addEventListener('keydown',e=>{if(e.key==='Enter')$('#githubAnalyze').click()});
  $('#checkUpdatesTop').addEventListener('click',()=>withToast(()=>rpc('checkUpdates'),false));$('#checkUpdatesPage').addEventListener('click',()=>withToast(()=>rpc('checkUpdates'),false));
  $('#updateAllBtn').addEventListener('click',()=>withToast(()=>rpc('updateAll'),false));
  $('#clearLogs').addEventListener('click',()=>rpc('clearLogs'));
  $('#copyLogs').addEventListener('click',async()=>{try{await navigator.clipboard.writeText($('#logView').textContent);toast(tr('copied'),'success')}catch{}});
  $('#cancelOperation').addEventListener('click',()=>fire('cancelOperation'));
  $('#forceRebuild').addEventListener('click',()=>withToast(()=>rpc('rebuild',{updateVencord:true}),false));
  $('#languageSelect').addEventListener('change',e=>{language=e.target.value;applyTranslations();saveAppSettings()});
  ['branchSelect','autoUpdateVencord','autoRestart','enableAfterInstall','devBuild'].forEach(id=>$('#'+id).addEventListener('change',saveAppSettings));
  $('#chooseDiscordLocation').addEventListener('click',async()=>{try{const r=await rpc('browseDiscordLocation');if(!r.path)return;state.customDiscordLocation=r.path;$('#customLocationText').textContent=r.path;await saveAppSettings()}catch(err){toast(err.message,'error')}});

  async function entryFiles(entry,prefix,out){
    if(entry.isFile){await new Promise((resolve,reject)=>entry.file(file=>{out.push({file,path:(prefix?prefix+'/':'')+file.name});resolve()},reject));return;}
    if(!entry.isDirectory)return;
    const nextPrefix=(prefix?prefix+'/':'')+entry.name,reader=entry.createReader();
    for(;;){const entries=await new Promise((resolve,reject)=>reader.readEntries(resolve,reject));if(!entries.length)break;for(const child of entries)await entryFiles(child,nextPrefix,out)}
  }
  function arrayBufferToBase64(buffer){const bytes=new Uint8Array(buffer);let binary='';const chunk=0x8000;for(let i=0;i<bytes.length;i+=chunk)binary+=String.fromCharCode.apply(null,bytes.subarray(i,Math.min(i+chunk,bytes.length)));return btoa(binary)}
  async function importDrop(dt){
    const found=[];
    if(dt.items&&dt.items.length){for(const item of dt.items){if(item.kind!=='file')continue;const entry=item.webkitGetAsEntry&&item.webkitGetAsEntry();if(entry)await entryFiles(entry,'',found);else{const f=item.getAsFile();if(f)found.push({file:f,path:f.name})}}}
    if(!found.length&&dt.files)for(const f of dt.files)found.push({file:f,path:f.name});
    if(!found.length)return;
    const total=found.reduce((n,x)=>n+x.file.size,0);if(total>16*1024*1024)throw new Error('Drag & Drop is limited to 16 MB. Use the Files/Folder buttons for larger packages.');
    const files=[];for(const x of found){const buf=await x.file.arrayBuffer();files.push({path:x.path,dataBase64:arrayBufferToBase64(buf)})}
    await rpc('importDroppedFiles',{files});
  }
  let dragDepth=0;
  window.addEventListener('dragenter',e=>{e.preventDefault();dragDepth++;$('#dropZone').classList.add('dragging')});
  window.addEventListener('dragleave',e=>{e.preventDefault();dragDepth--;if(dragDepth<=0){dragDepth=0;$('#dropZone').classList.remove('dragging')}});
  window.addEventListener('dragover',e=>{e.preventDefault();if(e.dataTransfer)e.dataTransfer.dropEffect='copy'});
  window.addEventListener('drop',async e=>{e.preventDefault();dragDepth=0;$('#dropZone').classList.remove('dragging');toast(tr('dragDetected'),'info');try{await importDrop(e.dataTransfer)}catch(err){toast(err.message||String(err),'error')}});
  document.addEventListener('keydown',e=>{if(e.key==='Escape'){if(!$('#modalRoot').innerHTML)return;closeModal()}});

  rpc('getState').then(s=>{state=s;renderState()}).catch(e=>toast(e.message,'error'));
})();
