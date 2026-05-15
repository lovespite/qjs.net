let s = "";
for (let i = 0; i < 200000; i++) {
    s += i.toString();
    if (s.length > 1000000) s = s.substring(s.length / 2);
}
console.log("String concat done, final length:", s.length);
