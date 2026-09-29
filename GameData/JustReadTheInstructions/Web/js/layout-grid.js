const STRIP_FRACTION_MIN = 0.2;
const STRIP_FRACTION_MAX = 1 / 3;
const STRIP_SWITCH_GAIN = 1.25;
const FILL_TIE_TOLERANCE = 0.01;

function keptFraction(rect, aspect) {
    const rectAspect = rect.width / rect.height;
    return Math.min(rectAspect, aspect) / Math.max(rectAspect, aspect);
}

function visibleArea(rects, aspect) {
    return rects.reduce((sum, rect) => sum + rect.width * rect.height * keptFraction(rect, aspect), 0);
}

function columnChoices(count, columns) {
    const all = Array.from({ length: count }, (_, i) => i + 1);
    return columns ? [Math.min(columns, count)] : all;
}

function bestGrid(count, width, height, gap, aspect, columns) {
    let best = { cols: 1, rows: count, tileWidth: 0 };
    for (const cols of columnChoices(count, columns)) {
        const rows = Math.ceil(count / cols);
        const tileWidth = Math.min(
            (width - gap * (cols - 1)) / cols,
            ((height - gap * (rows - 1)) / rows) * aspect,
        );
        if (tileWidth > best.tileWidth || best.tileWidth === 0) best = { cols, rows, tileWidth };
    }
    return { ...best, tileHeight: best.tileWidth / aspect };
}

function fitRects(count, box, { gap, aspect, columns }) {
    const { cols, rows, tileWidth, tileHeight } = bestGrid(count, box.width, box.height, gap, aspect, columns);
    const top = box.y + (box.height - rows * tileHeight - (rows - 1) * gap) / 2;

    return Array.from({ length: count }, (_, i) => {
        const row = Math.floor(i / cols);
        const tilesInRow = Math.min(cols, count - row * cols);
        const left = box.x + (box.width - tilesInRow * tileWidth - (tilesInRow - 1) * gap) / 2;
        return {
            x: left + (i % cols) * (tileWidth + gap),
            y: top + row * (tileHeight + gap),
            width: tileWidth,
            height: tileHeight,
        };
    });
}

function edge(start, length, parts, index) {
    return Math.round(start + (length * index) / parts);
}

function fillRectsWithColumns(count, cols, box) {
    const rows = Math.ceil(count / cols);
    return Array.from({ length: count }, (_, i) => {
        const row = Math.floor(i / cols);
        const col = i % cols;
        const tilesInRow = Math.min(cols, count - row * cols);
        const x = edge(box.x, box.width, tilesInRow, col);
        const y = edge(box.y, box.height, rows, row);
        return {
            x,
            y,
            width: edge(box.x, box.width, tilesInRow, col + 1) - x,
            height: edge(box.y, box.height, rows, row + 1) - y,
        };
    });
}

function fillRects(count, box, { aspect, columns }) {
    const candidates = columnChoices(count, columns).map((cols) => {
        const rects = fillRectsWithColumns(count, cols, box);
        return { rects, score: visibleArea(rects, aspect) };
    });
    const bestScore = Math.max(...candidates.map((c) => c.score));
    const closeEnough = candidates.filter((c) => c.score >= bestScore * (1 - FILL_TIE_TOLERANCE));
    return closeEnough[closeEnough.length - 1].rects;
}

function stripFraction(stripTiles) {
    return Math.min(STRIP_FRACTION_MAX, Math.max(STRIP_FRACTION_MIN, 1 / (stripTiles + 1)));
}

function splitOffStrip(box, gap, stripOnRight, fraction) {
    if (stripOnRight) {
        const stripWidth = Math.round(box.width * fraction);
        return {
            main: { ...box, width: box.width - stripWidth - gap },
            strip: { ...box, x: box.x + box.width - stripWidth, width: stripWidth },
        };
    }
    const stripHeight = Math.round(box.height * fraction);
    return {
        main: { ...box, height: box.height - stripHeight - gap },
        strip: { ...box, y: box.y + box.height - stripHeight, height: stripHeight },
    };
}

function stripLayout(side, stripTiles, box, options, arrange) {
    const { main, strip } = splitOffStrip(box, options.gap, side === 'right', stripFraction(stripTiles));
    const mainRect = arrange(1, main, options)[0];
    return { side, mainRect, strip, score: visibleArea([mainRect], options.aspect) };
}

function spotlightRects(count, spotlightIndex, box, gridOptions, arrange) {
    const options = { ...gridOptions, columns: null };
    const kept = stripLayout(options.stripSide, count - 1, box, options, arrange);
    const other = stripLayout(options.stripSide === 'right' ? 'bottom' : 'right', count - 1, box, options, arrange);
    const chosen = other.score > kept.score * STRIP_SWITCH_GAIN ? other : kept;
    const stripRects = arrange(count - 1, chosen.strip, options);

    const rects = Array.from({ length: count }, (_, i) => {
        if (i === spotlightIndex) return chosen.mainRect;
        return stripRects[i < spotlightIndex ? i : i - 1];
    });
    return { rects, stripSide: chosen.side };
}

export function layoutRects(count, spotlightIndex, box, options) {
    const arrange = options.fill ? fillRects : fitRects;
    if (count === 0) return { rects: [], stripSide: options.stripSide };
    if (spotlightIndex === null || count === 1) return { rects: arrange(count, box, options), stripSide: options.stripSide };
    return spotlightRects(count, spotlightIndex, box, options, arrange);
}
