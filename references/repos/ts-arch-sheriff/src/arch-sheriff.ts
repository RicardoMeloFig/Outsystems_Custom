import * as fs from "fs";
import * as tsviz from "./tsviz";

interface IModuleLaw {
    name: string;
    dependencies: string[];
}
interface HashSet {
    [key: string]: any;
}
type ModulesLaw = IModuleLaw[];

/** Reads the set of rules from the file in the path provided */
function getTheLaw(lawBookPath: string) {
    var expectedJSON = fs.readFileSync(lawBookPath, "utf8");
    var expected = JSON.parse(expectedJSON) as ModulesLaw;
    return expected;
}

/** gets the module dependencies and checks if they're according to the law */
function enforceTheLaw(targetDir: string, moduleLaw: ModulesLaw, rightsToRead: string) {
    var modules = tsviz.getModulesDependencies(targetDir);
    var problemFound = false;
    var expectedModulesMap: {[moduleName: string]: HashSet} = {};
	
    moduleLaw.forEach(function (module: IModuleLaw) {
        let dependencies: HashSet = {};
        module.dependencies.forEach(name => {
            dependencies[name] = null as string;
        });
        expectedModulesMap[module.name] = dependencies;
    });
    
    modules.forEach(function (module) {
        var expectedModuleDependencies = expectedModulesMap[module.name];
        if (!expectedModuleDependencies) {
            console.log(`error : Unknown module '${module.name}'.\n${rightsToRead}`);
            problemFound = true;
        }
        module.dependencies.forEach(function (dependency) {
            if (!(dependency in expectedModuleDependencies)) {
                console.log(`error : '${dependency}' dependency not allowed in module '${module.name}'.\n${rightsToRead}`);
                problemFound = true;
            }
        });
    });
    if (problemFound) {
        process.exit(1);
    }
}

// TODO: use proper argument parsing
var baseDir = process.argv[2];
var lawBook = baseDir + process.argv[3];
var rightsToRead = `Allowed modules and dependencies live in '${lawBook}'.
Before changing that file, validate it with a tech lead.
Creating new dependecies make it harder to test, may introduce circular
dependecy problems and will complicate code readability/maintenance.`;
    
enforceTheLaw(baseDir, getTheLaw(lawBook), rightsToRead);