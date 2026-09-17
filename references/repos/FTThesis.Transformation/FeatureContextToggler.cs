using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.UI.Mobile;
using OutSystems.Model.UI.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ModelAPITest
{
    class FeatureContextToggler : FeatureToggler
    {
        public void Run(string[] args)
        {
            string contents = File.ReadAllText(args[1]);

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            Console.WriteLine("----------Transformation Report----------");
            var p = deserializer.Deserialize<FeatureSet>(contents);
            /*foreach (Feature f in p.Features)
            {
                Console.WriteLine($"FEATURE: \n{f.Name}");
                foreach (String e in f.Elements)
                {
                    Console.WriteLine(e);
                }
            }*/

            var ESpacePath = args[2];

            if (!File.Exists(ESpacePath))
            {
                Console.WriteLine($"File {ESpacePath} not found");
                return;
            }

            var saveESpacePath = new FileInfo(args[3]);
            var outputDirectory = saveESpacePath.Directory.FullName;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var modelServices = OutSystems.ModelAPILoader.Loader.ModelServicesInstance;

            var module = modelServices.LoadESpace(ESpacePath);

            var isoldtraditional = IsTraditional(module);

            ToggleManager manager = new ToggleManager();

            if (isoldtraditional)
            {
                BlockTraditional traditionalBlocks = new BlockTraditional();
                ScreenTraditional s = new ScreenTraditional();
                ServerAction l = new ServerAction();
                //FTRemoteManagementAction t = new FTRemoteManagementAction();
                
                foreach (Feature f in p.Features)
                {
                    Console.WriteLine($"FEATURE: \n{f.Name} : FT_{module.Name}_{f.Name}");
                    traditionalBlocks.GetAllElementsFromList(module, f.Elements, f.Name);
                    s.GetAllElementsFromList(module, f.Elements, f.Name);
                    l.GetAllElementsFromList(module, f.Elements, f.Name);
                    Console.WriteLine("-----------------------------------------");
                }
                //t.GetToggleAction(module);
                
            }
            else
            {
                BlockReative reactiveBlocks = new BlockReative();
                ScreenReactive s = new ScreenReactive();
                ServerAction l = new ServerAction();
                //ClientAction c = new ClientAction();
                //FTRemoteManagementAction t = new FTRemoteManagementAction();
               
                foreach (Feature f in p.Features)
                {
                    Console.WriteLine($"FEATURE: \n{f.Name} : FT_{module.Name}_{f.Name}");
                    reactiveBlocks.GetAllElementsFromList(module, f.Elements, f.Name);
                    s.GetAllElementsFromList(module, f.Elements, f.Name);
                    l.GetAllElementsFromList(module, f.Elements, f.Name);
                    //c.GetAllElementsFromList(module, f.Elements, f.Name);
                    Console.WriteLine("-----------------------------------------");
                }
                //t.GetToggleAction(module);
                

            }
            manager.CreateActionToAddTogglesToMngPlat(module);
            module.Save(saveESpacePath.FullName);
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
