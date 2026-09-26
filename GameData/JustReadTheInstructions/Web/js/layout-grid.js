const STRIP_FRACTION = 0.2;
const FILL_TIE_TOLERANCE = 0.01;

function keptFraction(rect, aspect) {
    const rectAspect = rect.width / rect.height;
    return Math.min(rectAspect, aspect) / Math.max(rectAspect, aspect);
}

function visibleArea(rects, aspect) {
    return rects.reduce((sum, rect) => sum + rect.width * rect.height * keptFraction(rect, aspect), 0);
}

function bestGrid(count, width, height, gap, aspect) {
    let best = { cols: 1, rows: count, tileWidth: 0 };
    for (let cols = 1; cols <= count; cols++) {
        const rows = Math.ceil(count / cols);
        const tileWidth = Math.min(
            (width - gap * (cols - 1)) / cols,
            ((height - gap * (rows - 1)) / rows) * aspect,
        );
        if (tileWidth > best.tileWidth) best = { cols, rows, tileWidth };
    }
    return { ...best, tileHeight: best.tileWidth / aspect };
}

function fitRects(count, box, { gap, aspect }) {
    const { cols, rows, tileWidth, tileHeight } = bestGrid(count, box.width, box.height, gap, aspect);
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

function fillRects(count, box, { aspect }) {
    const candidates = Array.from({ length: count }, (_, i) => {
        const rects = fillRectsWithColumns(count, i + 1, box);
        return { rects, score: visibleArea(rects, aspect) };
    });
    const bestScore = Math.max(...candidates.map((c) => c.score));
    const closeEnough = candidates.filter((c) => c.score >= bestScore * (1 - FILL_TIE_TOLERANCE));
    return closeEnough[closeEnough.length - 1].rects;
}

function splitOffStrip(box, gap, stripOnRight) {
    if (stripOnRight) {
        const stripWidth = Math.round(box.width * STRIP_FRACTION);
        return {
            main: { ...box, width: box.width - stripWidth - gap },
            strip: { ...box, x: box.x + box.width - stripWidth, width: stripWidth },
        };
    }
    const stripHeight = Math.round(box.height * STRIP_FRACTION);
    return {
        main: { ...box, height: box.height - stripHeight - gap },
        strip: { ...box, y: box.y + box.height - stripHeight, height: stripHeight },
    };
}

function spotlightRects(count, spotlightIndex, box, options, arrange) {
    const [bottom, right] = [false, true].map((stripOnRight) => {
        const { main, strip } = splitOffStrip(box, options.gap, stripOnRight);
        const mainRect = arrange(1, main, options)[0];
        return { mainRect, strip, score: visibleArea([mainRect], options.aspect) };
    });
    const chosen = right.score > bottom.score ? right : bottom;
    const stripRects = arrange(count - 1, chosen.strip, options);

    return Array.from({ length: count }, (_, i) => {
        if (i === spotlightIndex) return chosen.mainRect;
        return stripRects[i < spotlightIndex ? i : i - 1];
    });
}

export function layoutRects(count, spotlightIndex, box, options) {
    if (count === 0) return [];
    const arrange = options.fill ? fillRects : fitRects;
    if (spotlightIndex === null || count === 1) return arrange(count, box, options);
    return spotlightRects(count, spotlightIndex, box, options, arrange);
}
