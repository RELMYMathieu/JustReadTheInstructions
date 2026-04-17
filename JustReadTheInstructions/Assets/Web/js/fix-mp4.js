function rd32(b, o) { return ((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]) >>> 0; }
function rd64(b, o) { return rd32(b, o) * 0x100000000 + rd32(b, o + 4); }
function wr16(b, o, v) { b[o] = (v >>> 8) & 0xFF; b[o + 1] = v & 0xFF; }
function wr32(b, o, v) { v >>>= 0; b[o] = v >>> 24; b[o + 1] = (v >>> 16) & 0xFF; b[o + 2] = (v >>> 8) & 0xFF; b[o + 3] = v & 0xFF; }
function wr64(b, o, v) { wr32(b, o, Math.floor(v / 0x100000000)); wr32(b, o + 4, v >>> 0); }

function scanMoov(d, s, e, state) {
    for (let i = s; i + 8 <= e;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > e) break;
        const t = rd32(d, i + 4);
        if (t === 0x6D766864) {
            const v0 = d[i + 8] === 0;
            state.mvhdOff = i; state.mvhdV0 = v0;
            state.movieTs = rd32(d, i + (v0 ? 20 : 28));
        } else if (t === 0x7472616B) {
            scanTrak(d, i + 8, i + sz, state);
        } else if (t === 0x6D766578) {
            scanMvex(d, i + 8, i + sz, state);
        }
        i += sz;
    }
}

function scanTrak(d, s, e, state) {
    for (let i = s; i + 8 <= e;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > e) break;
        const t = rd32(d, i + 4);
        if (t === 0x746B6864) {
            const v0 = d[i + 8] === 0;
            state.tkhdOff = i; state.tkhdV0 = v0;
            if (!state.trackId) {
                const idOff = i + (v0 ? 20 : 28);
                if (idOff + 4 <= i + sz) state.trackId = rd32(d, idOff);
            }
        } else if (t === 0x6D646961) {
            scanMdia(d, i + 8, i + sz, state);
        }
        i += sz;
    }
}

function scanMdia(d, s, e, state) {
    for (let i = s; i + 8 <= e;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > e) break;
        if (rd32(d, i + 4) === 0x6D646864) {
            const v0 = d[i + 8] === 0;
            state.mdhdOff = i; state.mdhdV0 = v0;
            state.trackTs = rd32(d, i + (v0 ? 20 : 28));
        }
        i += sz;
    }
}

function scanMvex(d, s, e, state) {
    for (let i = s; i + 8 <= e;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > e) break;
        if (rd32(d, i + 4) === 0x74726578) state.trexDefDur = rd32(d, i + 20);
        i += sz;
    }
}

function scanMoofDuration(d, s, e, trexDefDur) {
    let total = 0;
    for (let i = s; i + 8 <= e;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > e) break;
        if (rd32(d, i + 4) === 0x74726166) {
            let defDur = trexDefDur || 0;
            const trafEnd = i + sz;
            for (let j = i + 8; j + 8 <= trafEnd;) {
                const sz2 = rd32(d, j);
                if (sz2 < 8 || j + sz2 > trafEnd) break;
                const t2 = rd32(d, j + 4);
                if (t2 === 0x74666864) {
                    const fl = ((d[j + 9] << 16) | (d[j + 10] << 8) | d[j + 11]) >>> 0;
                    let off = j + 16;
                    if (fl & 1) off += 8; if (fl & 2) off += 4;
                    if (fl & 8) defDur = rd32(d, off);
                } else if (t2 === 0x7472756E) {
                    const fl = ((d[j + 9] << 16) | (d[j + 10] << 8) | d[j + 11]) >>> 0;
                    const count = rd32(d, j + 12);
                    let off = j + 16;
                    if (fl & 1) off += 4; if (fl & 4) off += 4;
                    const hasDur = !!(fl & 0x100);
                    const stride = (hasDur ? 4 : 0) + ((fl & 0x200) ? 4 : 0) + ((fl & 0x400) ? 4 : 0) + ((fl & 0x800) ? 4 : 0);
                    if (stride === 0) { j += sz2; continue; }
                    for (let k = 0; k < count && off + stride <= j + sz2; k++, off += stride)
                        total += hasDur ? rd32(d, off) : defDur;
                }
                j += sz2;
            }
        }
        i += sz;
    }
    return total;
}

function scanMoofIsRap(d, s, e) {
    for (let i = s; i + 8 <= e;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > e) break;
        if (rd32(d, i + 4) === 0x74726166) {
            let defFlags = 0;
            const trafEnd = i + sz;
            for (let j = i + 8; j + 8 <= trafEnd;) {
                const sz2 = rd32(d, j);
                if (sz2 < 8 || j + sz2 > trafEnd) break;
                const t2 = rd32(d, j + 4);
                if (t2 === 0x74666864) {
                    const fl = ((d[j + 9] << 16) | (d[j + 10] << 8) | d[j + 11]) >>> 0;
                    let off = j + 16;
                    if (fl & 1) off += 8; if (fl & 2) off += 4; if (fl & 8) off += 4;
                    if (fl & 0x10) off += 4;
                    if ((fl & 0x20) && off + 4 <= j + sz2) defFlags = rd32(d, off);
                } else if (t2 === 0x7472756E) {
                    const fl = ((d[j + 9] << 16) | (d[j + 10] << 8) | d[j + 11]) >>> 0;
                    let off = j + 16;
                    if (fl & 1) off += 4;
                    let firstFlags;
                    if ((fl & 4) && off + 4 <= j + sz2) {
                        firstFlags = rd32(d, off);
                    } else if (fl & 0x400) {
                        if (fl & 0x100) off += 4; if (fl & 0x200) off += 4;
                        firstFlags = off + 4 <= j + sz2 ? rd32(d, off) : defFlags;
                    } else {
                        firstFlags = defFlags;
                    }
                    return (firstFlags & 0x00010000) === 0;
                }
                j += sz2;
            }
        }
        i += sz;
    }
    return false;
}

function buildSidx(trackId, trackTs, frags) {
    const total = 32 + frags.length * 12;
    const b = new Uint8Array(total);
    let o = 0;
    wr32(b, o, total); o += 4;
    wr32(b, o, 0x73696478); o += 4;
    b[o++] = 0; b[o++] = 0; b[o++] = 0; b[o++] = 0;
    wr32(b, o, trackId); o += 4;
    wr32(b, o, trackTs); o += 4;
    wr32(b, o, 0); o += 4;
    wr32(b, o, 0); o += 4;
    b[o++] = 0; b[o++] = 0;
    wr16(b, o, frags.length); o += 2;
    for (const { size, dur, rap } of frags) {
        wr32(b, o, size & 0x7FFFFFFF); o += 4;
        wr32(b, o, dur); o += 4;
        wr32(b, o, rap ? 0x90000000 : 0); o += 4;
    }
    return b;
}

function writeDurMvhd(d, o, v0, dur) {
    const off = o + (v0 ? 24 : 32);
    if (v0) wr32(d, off, Math.min(dur, 0xFFFFFFFF) >>> 0);
    else wr64(d, off, dur);
}

function writeDurTkhd(d, o, v0, dur) {
    const off = o + (v0 ? 28 : 36);
    if (v0) wr32(d, off, Math.min(dur, 0xFFFFFFFF) >>> 0);
    else wr64(d, off, dur);
}

export async function fixMp4(blob) {
    const d = new Uint8Array(await blob.arrayBuffer());
    const state = {
        movieTs: 0, trackTs: 0, trackId: 1, trexDefDur: 0,
        mvhdOff: -1, tkhdOff: -1, mdhdOff: -1,
        mvhdV0: true, tkhdV0: true, mdhdV0: true,
    };
    let moovEnd = -1;
    const frags = [];
    let totalDur = 0;

    for (let i = 0; i + 8 <= d.length;) {
        const sz = rd32(d, i);
        if (sz < 8 || i + sz > d.length) break;
        const t = rd32(d, i + 4);
        if (t === 0x6D6F6F76) {
            moovEnd = i + sz;
            scanMoov(d, i + 8, moovEnd, state);
        } else if (t === 0x6D6F6F66) {
            const moofEnd = i + sz;
            const mdatSz = moofEnd + 8 <= d.length && rd32(d, moofEnd + 4) === 0x6D646174 ? rd32(d, moofEnd) : 0;
            const fragDur = scanMoofDuration(d, i + 8, moofEnd, state.trexDefDur);
            totalDur += fragDur;
            frags.push({ size: sz + mdatSz, dur: fragDur, rap: scanMoofIsRap(d, i + 8, moofEnd) });
        }
        i += sz;
    }

    if (moovEnd < 0 || state.movieTs === 0 || state.trackTs === 0) return blob;

    if (totalDur > 0 && state.mvhdOff >= 0) {
        const movieDur = Math.round(totalDur * state.movieTs / state.trackTs);
        writeDurMvhd(d, state.mvhdOff, state.mvhdV0, movieDur);
        if (state.tkhdOff >= 0) writeDurTkhd(d, state.tkhdOff, state.tkhdV0, movieDur);
        if (state.mdhdOff >= 0) writeDurMvhd(d, state.mdhdOff, state.mdhdV0, totalDur);
    }

    if (frags.length === 0) return new Blob([d], { type: blob.type });

    const sidx = buildSidx(state.trackId, state.trackTs, frags);
    const result = new Uint8Array(d.length + sidx.length);
    result.set(d.subarray(0, moovEnd), 0);
    result.set(sidx, moovEnd);
    result.set(d.subarray(moovEnd), moovEnd + sidx.length);

    return new Blob([result], { type: blob.type });
}