/*
Summary:
Script to bump the package version.

Workflow:
1. Create new branch 'bump/${version}'
2. Bump version on package.json
3. Update require.js file
4. Commit and push branch to GitHub
5. Create pull request in Github
*/

const execSync = require("child_process").execSync;
function exec(cmd) {
    execSync(cmd, { stdio: "inherit", env: process.env });
}

function execAndOutput(cmd) {
    return execSync(cmd, { env: process.env }).toString();
}

exec('git pull');

exec(`npm version prerelease --preid=outsystems --no-git-tag-version`);

const cwd = process.cwd();
const currentBranch = execAndOutput('git branch --no-color --show-current').replace(/[\r\n]+/, "");

const newVersionNumber = require('./../package.json').version;
const newBranch = `bump/requirejs-${newVersionNumber}`;

exec(`git checkout -b ${newBranch}`);

const {readFile, writeFile, promises: fsPromises} = require('fs');

readFile('./require.js', 'utf-8', function (err, contents) {
  if (err) {
    console.log(err);
    return;
  }

  const replaced = contents.replace(/version = '.*',/i, `version = '${newVersionNumber}',`);

  writeFile('./require.js', replaced, 'utf-8', function (err) {
    if (err) {
        console.log(err);
        return;
    }

    exec('git add -u');

    exec(`git commit -m "Bump version to ${newVersionNumber}"`);

    exec(`git push -u origin ${newBranch}`);

    exec(`gh pr create --title "Bump version to ${newVersionNumber}" --body "Bump package version to ${newVersionNumber}" --base ${currentBranch}`);
  });
});

process.chdir(cwd);