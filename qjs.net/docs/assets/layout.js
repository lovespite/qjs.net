// Shared sidebar + searchbar injection so each page only has to author its
// <main> content. Keeps the docs site fully static and file://-friendly.
(function () {
    const pages = [
        { group: 'Overview', items: [
            { href: 'index.html', title: '总览' },
            { href: 'getting-started.html', title: '快速上手' },
            { href: 'type-mapping.html', title: 'C# ↔ JS 类型映射' },
            { href: 'eventloop.html', title: '事件循环 & Promise' },
            { href: 'module-esm.html', title: 'ES Modules (import / export)' },
            { href: 'limitations.html', title: '当前版本局限' },
        ]},
        { group: 'Core modules', items: [
            { href: 'module-buffer.html', title: 'Buffer' },
            { href: 'module-stream.html', title: 'Stream' },
            { href: 'module-directorystream.html', title: 'DirectoryStream' },
        ]},
        { group: 'Built-in modules', items: [
            { href: 'module-fs.html', title: 'fs (sync)' },
            { href: 'module-fsAsync.html', title: 'fsAsync' },
            { href: 'module-fetch.html', title: 'fetch' },
            { href: 'module-timers.html', title: 'Timers' },
            { href: 'module-encoder.html', title: 'TextEncoder / TextDecoder' },
        ]},
    ];

    const here = (location.pathname.split('/').pop() || 'index.html').toLowerCase();

    const sb = document.createElement('aside');
    sb.className = 'sidebar';
    sb.innerHTML =
        '<h1>QuickJsNet</h1>' +
        '<div class="ver">流式 IO + 原生代理</div>' +
        '<nav>' +
        pages.map(g =>
            `<div class="group">${g.group}</div>` +
            g.items.map(it =>
                `<a href="${it.href}"${it.href.toLowerCase() === here ? ' class="active"' : ''}>${it.title}</a>`
            ).join('')
        ).join('') +
        '</nav>';

    const body = document.body;
    const layout = document.createElement('div');
    layout.className = 'layout';

    // Move existing <main> into the layout container (or wrap body if none).
    const main = document.querySelector('main') || (() => {
        const m = document.createElement('main');
        while (body.firstChild) m.appendChild(body.firstChild);
        return m;
    })();
    if (main.parentNode === body) body.removeChild(main);

    // Prepend search bar to the main content.
    const search = document.createElement('div');
    search.className = 'searchbar';
    search.innerHTML =
        '<input id="docsearch" type="text" placeholder="搜索关键词（Buffer、pipe、fetch、async…）" />' +
        '<span class="hint">输入即时过滤本页 + 全站</span>';
    const results = document.createElement('div');
    results.id = 'search-results';
    results.className = 'search-results hidden';
    main.insertBefore(results, main.firstChild);
    main.insertBefore(search, results);

    const footer = document.createElement('footer');
    footer.innerHTML = 'QuickJsNet 文档 · 静态 HTML · 可直接 file:// 打开';
    main.appendChild(footer);

    layout.appendChild(sb);
    layout.appendChild(main);
    body.appendChild(layout);
})();
