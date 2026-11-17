// Radix-2 complex FFT (in place, Float64Array) and FFT convolution. Analysis/offline use only.
export function fft(re, im, inverse = false) {
  const n = re.length;
  for (let i = 1, j = 0; i < n; i++) {
    let bit = n >> 1;
    for (; j & bit; bit >>= 1) j ^= bit;
    j ^= bit;
    if (i < j) { let t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
  }
  for (let len = 2; len <= n; len <<= 1) {
    const ang = (2 * Math.PI / len) * (inverse ? 1 : -1);
    const wr = Math.cos(ang), wi = Math.sin(ang), half = len >> 1;
    for (let i = 0; i < n; i += len) {
      let cr = 1, ci = 0;
      for (let k = 0; k < half; k++) {
        const a = i + k, b = a + half;
        const xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
        const t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
      }
    }
  }
  if (inverse) for (let i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
}
export const nextPow2 = (n) => { let p = 1; while (p < n) p <<= 1; return p; };

// Linear convolution of x (Float32Array) with each IR channel, scaled; output length x.length (truncated).
export function convolve(x, irs, scale = 1, outLen = x.length) {
  const N = nextPow2(x.length + irs[0].length);
  const xr = new Float64Array(N), xi = new Float64Array(N);
  xr.set(x); fft(xr, xi);
  return irs.map((h) => {
    const hr = new Float64Array(N), hi = new Float64Array(N);
    hr.set(h); fft(hr, hi);
    for (let k = 0; k < N; k++) { const r = xr[k] * hr[k] - xi[k] * hi[k], i = xr[k] * hi[k] + xi[k] * hr[k]; hr[k] = r; hi[k] = i; }
    fft(hr, hi, true);
    const y = new Float32Array(outLen);
    for (let i = 0; i < outLen; i++) y[i] = hr[i] * scale;
    return y;
  });
}
