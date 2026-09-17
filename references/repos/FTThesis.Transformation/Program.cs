using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.Data;
using OutSystems.Model.Logic;
using OutSystems.Model.UI;
using OutSystems.Model.UI.Mobile.Widgets;
using OutSystems.Model.UI.Web;
using ServiceStudio;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ModelAPITest
{
    
    class Program
    {

        static void Main(string[] args)
        {
            
            if (args[0] != "diff" && args[0] != "all" && args[0] != "features")
            {
                Console.WriteLine("Usage: \nall <File.oml> <outputFile.oml> \nOR \ndiff <oldFile.oml> <newFile.oml> <outputFile.oml> \nOR \nfeatures <configFile.yml> <moduleFile.oml> <outputFile.oml>");
                return;
            }
            if (args[0] == "diff" & args.Length != 4)
            {
                Console.WriteLine("Usage: diff <oldFile.oml> <newFile.oml> <outputFile.oml>");
                return;
            }
            if (args[0] == "all" & args.Length != 3)
            {
                Console.WriteLine("Usage: all <File.oml> <outputFile.oml>");
                return;
            }

            if (args[0] == "features" && args.Length != 4)
            {
                Console.WriteLine("Usage: features <configFile.yml> <moduleFile.oml> <outputFile.oml>");
                return;
            }

            FileStream filestream = new FileStream("ReportFile.txt", FileMode.Create);
            var streamwriter = new StreamWriter(filestream);
            streamwriter.AutoFlush = true;
            Console.SetOut(streamwriter);

            if (args[0] == "diff" & args.Length == 4)
            {
                var runDiff = new DifferentElementsToggler();
                runDiff.Run(args);
            }

            if (args[0] == "all" & args.Length == 3)
            {
                var runAll = new AllElementsToggler();
                runAll.Run(args);
            }

             if (args[0] == "features" & args.Length == 4)
            {
                var runContext = new FeatureContextToggler();
                runContext.Run(args);
            }

        }

        

    }
}
