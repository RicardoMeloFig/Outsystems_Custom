import * as tsviz from "./tsviz"; 

function main(args: string[]) {
    let switches = args.filter(a => a.indexOf("-") === 0);
    let nonSwitches = args.filter(a => a.indexOf("-") !== 0);
    
    if (nonSwitches.length < 1) {
        console.error(
            "Invalid number of arguments. Usage:\n" + 
            "  <switches> <sources filename/directory> <output.png>\n" +
            "Available switches:\n" +
            "  -dependencies: produces the modules' dependencies diagram");
        return;
    }
    
    console.log(tsviz.getModules(args[0]))
    
}

export function run() {
    main(process.argv.slice(2));
}
