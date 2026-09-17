using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.UI.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ModelAPITest
{
    class DifferentElementsToggler : FeatureToggler
    {
        public void Run(string[] args)
        {
            var oldESpacePath = args[1];
            var newESpacePath = args[2];

            if (!File.Exists(oldESpacePath))
            {
                Console.WriteLine($"File {oldESpacePath} not found");
                return;
            }
            if (!File.Exists(newESpacePath))
            {
                Console.WriteLine($"File {newESpacePath} not found");
                return;
            }

            var saveESpacePath = new FileInfo(args[3]);
            var outputDirectory = saveESpacePath.Directory.FullName;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var modelServices = OutSystems.ModelAPILoader.Loader.ModelServicesInstance;

            var oldmodule = modelServices.LoadESpace(oldESpacePath);
            var newmodule = modelServices.LoadESpace(newESpacePath);

            var isoldtraditional = IsTraditional(oldmodule);
            var isnewtraditional = IsTraditional(newmodule);

            if (isoldtraditional != isnewtraditional)
            {
                Console.WriteLine("<oldFile.oml> and <newFile.oml> are not compatible");
                return;
            }

            Console.WriteLine("----------Transformation Report----------");

            ToggleManager manager = new ToggleManager();

            if (isoldtraditional)
            {
                BlockTraditional tradicionalBlocks = new BlockTraditional();
                ScreenTraditional s = new ScreenTraditional();
                ServerAction l = new ServerAction();
                tradicionalBlocks.GetDiffElements(oldmodule, newmodule, "new");
                s.GetDiffElements(oldmodule, newmodule, "new");
                l.GetDiffElements(oldmodule, newmodule, "new");
                //FTRemoteManagementAction t = new FTRemoteManagementAction();
                //t.GetToggleAction(newmodule);

            }
            else
            {
                BlockReative reactiveBlocks = new BlockReative();
                ScreenReactive s = new ScreenReactive();
                ServerAction l = new ServerAction();
                //ClientAction c = new ClientAction();
                reactiveBlocks.GetDiffElements(oldmodule, newmodule, "new");
                s.GetDiffElements(oldmodule, newmodule, "new");
                l.GetDiffElements(oldmodule, newmodule, "new");
                //c.GetDiffElements(oldmodule, newmodule, "new");
                //FTRemoteManagementAction t = new FTRemoteManagementAction();
                //t.GetToggleAction(newmodule);
            }
            manager.CreateActionToAddTogglesToMngPlat(newmodule);
            newmodule.Save(saveESpacePath.FullName);
            //Console.WriteLine($"\nESpace saved to {saveESpacePath.FullName}");
        }

        public bool IsTraditional(IESpace module)

        {
            var themes = module.GetAllDescendantsOfType<IWebTheme>();
            bool any = false;
            foreach (IWebTheme tm in themes)
            {
                any = true;
            }
            return any;
        }
    }
}
