const size = 1000000;
const arr = [];
for (let i = 0; i < size; i++) {
    arr.push(Math.random());
}
arr.sort();
console.log("Sorted array size:", arr.length, "First element:", arr[0]);
