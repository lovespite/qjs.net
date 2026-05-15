import { readLine, readKey, clear, write } from 'qjs:console';

write("Welcome to QuickJS.NET CLI!\n");
write("Press any key to clear the screen...");
let key = readKey(true);
console.log("\nYou pressed:", key.keyChar, "(", key.key, ")");

clear();
write("What is your name? ");
let name = readLine();
write(`Hello, ${name}! Welcome to the interactive console.\n`);
write("Press ESC to exit.\n");

while (true) {
    let k = readKey(true);
    if (k.key === "Escape") break;
    write(`You pressed: ${k.keyChar} (${k.key})\n`);
}
