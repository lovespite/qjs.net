function mandelbrot(x, y) {
    var cr = y - 0.5;
    var ci = x;
    var zi = 0.0;
    var zr = 0.0;
    var i = 0;
    while (i < 50 && (zr * zr + zi * zi) < 4.0) {
        var tr = zr * zr - zi * zi + cr;
        var ti = 2 * zr * zi + ci;
        zr = tr;
        zi = ti;
        i++;
    }
    return i;
}

let sum = 0;
for (let y = -1; y <= 1; y += 0.005) {
    for (let x = -1; x <= 1; x += 0.005) {
        sum += mandelbrot(x, y);
    }
}
console.log("Mandelbrot sum:", sum);
