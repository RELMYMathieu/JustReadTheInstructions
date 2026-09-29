export class FeedCanvas {
    constructor(className, { onDraw, onResize } = {}) {
        this.el = document.createElement('canvas');
        this.el.className = className;
        this._ctx = this.el.getContext('2d');
        this._onDraw = onDraw;
        this._onResize = onResize;
        this._pendingFrame = null;
        this._decoding = false;
        this._generation = 0;
    }

    push(frame) {
        this._pendingFrame = new Blob([frame], { type: 'image/jpeg' });
        if (!this._decoding) this._drawPendingFrames();
    }

    clear() {
        this._generation++;
        this._pendingFrame = null;
        this._ctx.clearRect(0, 0, this.el.width, this.el.height);
    }

    async _drawPendingFrames() {
        this._decoding = true;
        while (this._pendingFrame) {
            const blob = this._pendingFrame;
            const generation = this._generation;
            this._pendingFrame = null;
            try {
                const bitmap = await createImageBitmap(blob);
                if (generation === this._generation) this._draw(bitmap);
                bitmap.close();
            } catch { }
        }
        this._decoding = false;
    }

    _draw(bitmap) {
        if (this.el.width !== bitmap.width || this.el.height !== bitmap.height) {
            this.el.width = bitmap.width;
            this.el.height = bitmap.height;
            this._onResize?.(bitmap.width, bitmap.height);
        }
        this._ctx.drawImage(bitmap, 0, 0);
        this._onDraw?.();
    }
}
