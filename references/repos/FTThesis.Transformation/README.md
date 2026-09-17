# Feature Toggle Thesis - Transformation
Program for transforming modules and introducing Feature Toggles, in light of Feature Toggle Thesis for FCT-UNL

## Description

You can use the test files to test the program. The files that are up to date are stored in the folder **CurrentTF**. The folder TestFiles has older files for testing previous versions of this program and may not work properly.

This program encapsulates blocks, screens, server actions and client actions in feature toggles.

The current program is written for both **Traditional Web** and **Reactive**. Keep in mind that both omls (if running for differential between versions) must be in the same language.

## Running

For running, execute the program and then insert the desired file paths, as well as the path for the output file, according to instructions:

diff <oldFile.oml> <newFile.oml> <outputFile.oml> - for inserting toggles in new elements between old version and new version of the same OML

OR

features <configFile.yml> <moduleFile.oml> <outputFile.oml> - for inserting toggles in an OML according to a config file specifying feature context

## Branches

**Master** is the main program, running the thesis base transformation

**FullApplications** runs the transformation for an application and not only one module. This composes thesis' future work.

