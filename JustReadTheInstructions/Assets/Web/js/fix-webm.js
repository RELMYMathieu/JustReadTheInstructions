const EBML = {
    EBML_ID: 0x1A45DFA3,
    SEGMENT: 0x18538067,
    SEG_INFO: 0x1549A966,
    DURATION: 0x4489,
    TIMECODE_SCALE: 0x2AD7B1,
    CLUSTER: 0x1F43B675,
    CLUSTER_TIMECODE: 0xE7,
    SIMPLE_BLOCK: 0xA3,
    BLOCK_GROUP: 0xA0,
    BLOCK: 0xA1,
    CUES: 0x1C53BB6B,
    CUE_POINT: 0xBB,
    CUE_TIME: 0xB3,
    CUE_TRACK_POSITIONS: 0xB7,
    CUE_TRACK: 0xF7,
    CUE_CLUSTER_POS: 0xF1,
    VOID: 0xEC,
    SEEK_HEAD: 0x114D9B74,
    TRACKS: 0x1654AE6B,
    TRACK_ENTRY: 0xAE,
    TRACK_TYPE: 0x83,
    DEFAULT_DURATION: 0x23E383,
};

function readVint(d, offset) {
    if (offset >= d.length) return null;
    const b = d[offset];
    if (b === 0) return null;
    let width = 1;
    let mask = 0x80;
    while (!(b & mask) && width <= 8) { width++; mask >>= 1; }
    if (width > 8 || offset + width > d.length) return null;
    let val = b & (mask - 1);
    for (let i = 1; i < width; i++) val = (val * 256) + d[offset + i];
    return { val, len: width };
}

function readId(d, offset) {
    if (offset >= d.length) return null;
    const b = d[offset];
    if (b === 0) return null;
    let width = 1;
    let mask = 0x80;
    while (!(b & mask) && width <= 4) { width++; mask >>= 1; }
    if (offset + width > d.length) return null;
    let id = b;
    for (let i = 1; i < width; i++) id = (id * 256) + d[offset + i];
    return { id, len: width };
}

function readSize(d, offset) {
    return readVint(d, offset);
}

function writeVintFixed(val, width) {
    const b = new Uint8Array(width);
    const marker = 0x80 >> (width - 1);
    let v = val;
    for (let i = width - 1; i > 0; i--) { b[i] = v & 0xFF; v = Math.floor(v / 256); }
    b[0] = (v & (marker - 1)) | marker;
    return b;
}

function writeUint(val) {
    if (val <= 0) return new Uint8Array([0]);
    let byteLen = 1;
    let tmp = val;
    while (tmp > 255) { byteLen++; tmp = Math.floor(tmp / 256); }
    const b = new Uint8Array(byteLen);
    let v = val;
    for (let i = byteLen - 1; i >= 0; i--) { b[i] = v & 0xFF; v = Math.floor(v / 256); }
    return b;
}

function writeId(id) {
    if (id <= 0xFF) return new Uint8Array([id]);
    if (id <= 0xFFFF) return new Uint8Array([id >> 8, id & 0xFF]);
    if (id <= 0xFFFFFF) return new Uint8Array([id >> 16, (id >> 8) & 0xFF, id & 0xFF]);
    return new Uint8Array([(id >>> 24) & 0xFF, (id >> 16) & 0xFF, (id >> 8) & 0xFF, id & 0xFF]);
}

function encodeFloat64(v) {
    const buf = new ArrayBuffer(8);
    new DataView(buf).setFloat64(0, v, false);
    return new Uint8Array(buf);
}

function buildElement(id, payload) {
    const idBytes = writeId(id);
    const sizeBytes = writeVintFixed(payload.length, vintWidth(payload.length));
    const out = new Uint8Array(idBytes.length + sizeBytes.length + payload.length);
    out.set(idBytes, 0);
    out.set(sizeBytes, idBytes.length);
    out.set(payload, idBytes.length + sizeBytes.length);
    return out;
}

function vintWidth(val) {
    if (val < 0x7F) return 1;
    if (val < 0x3FFF) return 2;
    if (val < 0x1FFFFF) return 3;
    if (val < 0x0FFFFFFF) return 4;
    return 8;
}

function concat(...arrays) {
    const total = arrays.reduce((s, a) => s + a.length, 0);
    const out = new Uint8Array(total);
    let off = 0;
    for (const a of arrays) { out.set(a, off); off += a.length; }
    return out;
}

function scanSegment(d, segStart, segDataStart, segDataEnd) {
    const clusters = [];
    let segInfoStart = -1, segInfoEnd = -1;
    let tracksStart = -1, tracksEnd = -1;
    let frameCount = 0;
    let cuesStart = -1, cuesEnd = -1;
    let seekHeadStart = -1, seekHeadEnd = -1;
    let timecodeScale = 1000000;

    let i = segDataStart;
    while (i < segDataEnd && i < d.length) {
        const idR = readId(d, i);
        if (!idR) break;
        const szR = readSize(d, i + idR.len);
        if (!szR) break;

        const eStart = i;
        const dataStart = i + idR.len + szR.len;
        const eEnd = dataStart + szR.val;

        if (idR.id === EBML.SEG_INFO) {
            segInfoStart = eStart;
            segInfoEnd = Math.min(eEnd, d.length);
            let j = dataStart;
            while (j < segInfoEnd) {
                const iR2 = readId(d, j);
                if (!iR2) break;
                const sR2 = readSize(d, j + iR2.len);
                if (!sR2) break;
                if (iR2.id === EBML.TIMECODE_SCALE) {
                    let tv = 0;
                    for (let k = 0; k < sR2.val; k++) tv = tv * 256 + d[j + iR2.len + sR2.len + k];
                    timecodeScale = tv || 1000000;
                }
                j += iR2.len + sR2.len + sR2.val;
            }
        }

        if (idR.id === EBML.TRACKS) {
            tracksStart = eStart;
            tracksEnd = Math.min(eEnd, d.length);
        }

        if (idR.id === EBML.CLUSTER) {
            let clusterTime = 0;
            let k = dataStart;
            while (k < Math.min(eEnd, d.length)) {
                const bIdR = readId(d, k);
                if (!bIdR) break;
                const bSzR = readSize(d, k + bIdR.len);
                if (!bSzR) break;
                if (bIdR.id === EBML.SIMPLE_BLOCK || bIdR.id === EBML.BLOCK) frameCount++;
                if (bIdR.id === EBML.CLUSTER_TIMECODE) {
                    let tv = 0;
                    for (let m = 0; m < bSzR.val; m++) tv = tv * 256 + d[k + bIdR.len + bSzR.len + m];
                    clusterTime = tv;
                }
                k += bIdR.len + bSzR.len + bSzR.val;
            }
            clusters.push({ pos: eStart - segDataStart, time: clusterTime, end: Math.min(eEnd, d.length) });
        }

        if (idR.id === EBML.CUES) {
            cuesStart = eStart;
            cuesEnd = Math.min(eEnd, d.length);
        }

        if (idR.id === EBML.SEEK_HEAD) {
            seekHeadStart = eStart;
            seekHeadEnd = Math.min(eEnd, d.length);
        }

        i = eEnd;
    }

    return { clusters, segInfoStart, segInfoEnd, cuesStart, cuesEnd, timecodeScale, seekHeadStart, seekHeadEnd, tracksStart, tracksEnd, frameCount };
}

function patchTracksDefaultDuration(d, tracksStart, tracksEnd, defaultDurationNs) {
    const idR = readId(d, tracksStart);
    const szR = readSize(d, tracksStart + idR.len);
    const dataStart = tracksStart + idR.len + szR.len;

    const durEl = buildElement(EBML.DEFAULT_DURATION, writeUint(defaultDurationNs));
    const parts = [];
    let j = dataStart;

    while (j < tracksEnd) {
        const eStart = j;
        const eIdR = readId(d, j);
        if (!eIdR) break;
        const eSzR = readSize(d, j + eIdR.len);
        if (!eSzR) break;
        const eDataStart = j + eIdR.len + eSzR.len;
        const eEnd = eDataStart + eSzR.val;

        if (eIdR.id === EBML.TRACK_ENTRY) {
            let isVideo = false;
            let hasDur = false;
            let k = eDataStart;
            while (k < Math.min(eEnd, tracksEnd)) {
                const cIdR = readId(d, k);
                if (!cIdR) break;
                const cSzR = readSize(d, k + cIdR.len);
                if (!cSzR) break;
                if (cIdR.id === EBML.TRACK_TYPE && d[k + cIdR.len + cSzR.len] === 1) isVideo = true;
                if (cIdR.id === EBML.DEFAULT_DURATION) hasDur = true;
                k += cIdR.len + cSzR.len + cSzR.val;
            }

            if (isVideo && !hasDur) {
                const entryPayload = concat(d.subarray(eDataStart, Math.min(eEnd, tracksEnd)), durEl);
                parts.push(buildElement(EBML.TRACK_ENTRY, entryPayload));
                j = Math.min(eEnd, tracksEnd);
                continue;
            }
        }

        parts.push(d.subarray(eStart, Math.min(eEnd, tracksEnd)));
        j = Math.min(eEnd, tracksEnd);
    }

    return buildElement(EBML.TRACKS, concat(...parts));
}

function buildCues(clusters, clusterShift) {
    const cuePoints = clusters.map(c => {
        const pos = Math.max(0, c.pos + clusterShift);
        const trackPos = concat(
            buildElement(EBML.CUE_TRACK, writeUint(1)),
            buildElement(EBML.CUE_CLUSTER_POS, writeUint(pos))
        );
        return buildElement(EBML.CUE_POINT, concat(
            buildElement(EBML.CUE_TIME, writeUint(c.time)),
            buildElement(EBML.CUE_TRACK_POSITIONS, trackPos)
        ));
    });
    return buildElement(EBML.CUES, concat(...cuePoints));
}

function patchSegInfo(d, segInfoStart, segInfoEnd, duration) {
    const durationEl = buildElement(EBML.DURATION, encodeFloat64(duration));

    const idR = readId(d, segInfoStart);
    const szR = readSize(d, segInfoStart + idR.len);
    const dataStart = segInfoStart + idR.len + szR.len;

    let existingDurationStart = -1;
    let existingDurationEnd = -1;

    let j = dataStart;
    while (j < segInfoEnd) {
        const childStart = j;
        const iR2 = readId(d, j);
        if (!iR2) break;
        const sR2 = readSize(d, j + iR2.len);
        if (!sR2) break;

        if (iR2.id === EBML.DURATION) {
            existingDurationStart = childStart;
            existingDurationEnd = j + iR2.len + sR2.len + sR2.val;
            break;
        }
        j += iR2.len + sR2.len + sR2.val;
    }

    let oldPayload;
    if (existingDurationStart !== -1) {
        oldPayload = concat(
            d.subarray(dataStart, existingDurationStart),
            d.subarray(existingDurationEnd, segInfoEnd)
        );
    } else {
        oldPayload = d.subarray(dataStart, segInfoEnd);
    }

    const newPayload = concat(oldPayload, durationEl);
    return buildElement(EBML.SEG_INFO, newPayload);
}

export async function fixWebm(blob) {
    if (blob.size > 2 * 1024 * 1024 * 1024) {
        console.warn("File is too large to process in browser memory.");
        return blob;
    }

    const buf = await blob.arrayBuffer();
    const d = new Uint8Array(buf);

    let i = 0;
    const idR = readId(d, i);
    if (!idR || idR.id !== EBML.EBML_ID) return blob;
    const szR = readSize(d, i + idR.len);
    if (!szR) return blob;
    const ebmlEnd = i + idR.len + szR.len + szR.val;

    i = ebmlEnd;
    const segIdR = readId(d, i);
    if (!segIdR || segIdR.id !== EBML.SEGMENT) return blob;

    const segSizeStart = i + segIdR.len;
    const segSzR = readSize(d, segSizeStart);
    if (!segSzR) return blob;

    const segDataStart = segSizeStart + segSzR.len;

    let isUnknownSize = false;
    if (segSzR.len === 8) {
        let allFF = true;
        for (let k = 1; k < 8; k++) {
            if (d[segSizeStart + k] !== 0xFF) allFF = false;
        }
        isUnknownSize = allFF;
    }

    const segDataEnd = isUnknownSize ? d.length : segDataStart + segSzR.val;

    const { clusters, segInfoStart, segInfoEnd, cuesStart, cuesEnd,
        seekHeadStart, seekHeadEnd, tracksStart, tracksEnd,
        frameCount, timecodeScale } = scanSegment(d, i, segDataStart, segDataEnd);

    if (clusters.length === 0 || segInfoStart < 0) return blob;

    let lastTime = clusters[clusters.length - 1].time;
    let ci = clusters[clusters.length - 1].pos + segDataStart;

    const cidR = readId(d, ci);
    if (cidR) {
        const cszR = readSize(d, ci + cidR.len);
        if (cszR) {
            ci += cidR.len + cszR.len;

            while (ci < Math.min(clusters[clusters.length - 1].end, d.length)) {
                const iR2 = readId(d, ci);
                if (!iR2) break;
                const sR2 = readSize(d, ci + iR2.len);
                if (!sR2) break;

                if (iR2.id === EBML.SIMPLE_BLOCK || iR2.id === EBML.BLOCK) {
                    const vtR = readVint(d, ci + iR2.len + sR2.len);
                    if (vtR) {
                        const relTime = (d[ci + iR2.len + sR2.len + vtR.len] << 8) | d[ci + iR2.len + sR2.len + vtR.len + 1];
                        const signedTime = relTime >= 32768 ? relTime - 65536 : relTime;
                        const absTime = clusters[clusters.length - 1].time + signedTime;
                        if (absTime > lastTime) lastTime = absTime;
                    }
                }
                ci += iR2.len + sR2.len + sR2.val;
            }
        }
    }

    const durationTick = lastTime + 33;
    const newInfoEl = patchSegInfo(d, segInfoStart, segInfoEnd, durationTick);

    const hasCues = cuesStart >= 0;
    const clusterAbsStart = segDataStart + clusters[0].pos;
    const seekHeadSize = seekHeadStart !== -1 ? (seekHeadEnd - seekHeadStart) : 0;
    const infoSizeDiff = newInfoEl ? newInfoEl.length - (segInfoEnd - segInfoStart) : 0;
    const cuesBeforeClustersSize = (hasCues && cuesStart < clusterAbsStart) ? (cuesEnd - cuesStart) : 0;

    const defaultDurationNs = frameCount > 1 ? Math.round(durationTick * timecodeScale / frameCount) : 0;
    const newTracksEl = (tracksStart !== -1 && defaultDurationNs > 0)
        ? patchTracksDefaultDuration(d, tracksStart, tracksEnd, defaultDurationNs)
        : null;

    const tracksSizeDiff = newTracksEl ? newTracksEl.length - (tracksEnd - tracksStart) : 0;
    const clusterShift = infoSizeDiff + tracksSizeDiff - seekHeadSize - cuesBeforeClustersSize;

    const cuesEl = buildCues(clusters, clusterShift);

    const payloadParts = [];

    if (seekHeadStart !== -1) {
        payloadParts.push(d.subarray(segDataStart, seekHeadStart));
        payloadParts.push(d.subarray(seekHeadEnd, segInfoStart));
    } else {
        payloadParts.push(d.subarray(segDataStart, segInfoStart));
    }

    payloadParts.push(newInfoEl || d.subarray(segInfoStart, segInfoEnd));

    if (hasCues && cuesStart < clusterAbsStart) {
        payloadParts.push(d.subarray(segInfoEnd, cuesStart));
        payloadParts.push(d.subarray(cuesEnd, clusterAbsStart));
    } else {
        if (newTracksEl && tracksStart !== -1) {
            payloadParts.push(d.subarray(segInfoEnd, tracksStart));
            payloadParts.push(newTracksEl);
            payloadParts.push(d.subarray(tracksEnd, clusterAbsStart));
        } else {
            payloadParts.push(d.subarray(segInfoEnd, clusterAbsStart));
        }
    }

    if (hasCues && cuesStart >= clusterAbsStart) {
        payloadParts.push(d.subarray(clusterAbsStart, cuesStart));
        payloadParts.push(d.subarray(cuesEnd));
    } else {
        payloadParts.push(d.subarray(clusterAbsStart));
    }

    payloadParts.push(cuesEl);

    const totalPayloadSize = payloadParts.reduce((s, a) => s + a.length, 0);
    const newSegSizeBytes = writeVintFixed(totalPayloadSize, 8);

    const result = concat(d.subarray(0, segSizeStart), newSegSizeBytes, ...payloadParts);
    return new Blob([result], { type: blob.type });
}