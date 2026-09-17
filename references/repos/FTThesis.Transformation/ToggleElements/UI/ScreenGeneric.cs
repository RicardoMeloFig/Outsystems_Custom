using ModelAPITest.ToggleElements;
using OutSystems.Model;
using OutSystems.Model.Logic.Nodes;
using OutSystems.Model.UI;
using OutSystems.Model.UI.Web;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModelAPITest
{
    abstract class ScreenGeneric<GLink, GScreen, GParent1, GParent2> : ToggleableElement
        where GLink : IObjectSignature
        where GScreen : IScreen
        where GParent1 : IObjectSignature
        where GParent2 : IObjectSignature
    {
        public void GetAllElements(IESpace newe)
        {
            var listScreens = newe.GetAllDescendantsOfType<GScreen>();

            List<IKey> screensKeys = new List<IKey>();
            Console.WriteLine("Transformed Screens:");
            foreach (GScreen screen in listScreens)
            {
                Console.WriteLine(screen);
                screensKeys.Add(screen.ObjectKey);

            }

            if (screensKeys.Count() != 0)
            {
                ToggleElement(newe, screensKeys, "defaultfeature");
                CreateScreenPrep(newe, screensKeys, "defaultfeature");
            }
        }

        public void GetAllElementsFromList(IESpace newe, List<string> elements, String feature)
        {
            var listScreens = newe.GetAllDescendantsOfType<GScreen>().Where(b => elements.Contains(b.Name)); 

            List<IKey> screensKeys = new List<IKey>();
            Console.WriteLine("Transformed Screens:");
            foreach (GScreen screen in listScreens)
            {
                Console.WriteLine(screen);
                screensKeys.Add(screen.ObjectKey);

            }

            if (screensKeys.Count() != 0)
            {
                ToggleElement(newe, screensKeys, feature);
                CreateScreenPrep(newe, screensKeys, feature);
            }
        }

        public void GetDiffElements(IESpace old, IESpace newe, string newOrAltered)
        {
            var listOldScreens = old.GetAllDescendantsOfType<GScreen>();

            var listNewScreens = newe.GetAllDescendantsOfType<GScreen>();

            List<GScreen> difScreens = new List<GScreen>();
            List<IKey> difScreensKeys = new List<IKey>();

            foreach (GScreen screen in listNewScreens)
            {
                var skey = screen.ObjectKey;
                var modDate = screen.LastModifiedDate;
                if (newOrAltered.Equals("new"))
                {
                    var olds = listOldScreens.SingleOrDefault(s => (s.ObjectKey.Equals(skey)));
                    if (olds == null)
                    {
                        difScreens.Add(screen);
                        difScreensKeys.Add(screen.ObjectKey);
                    }
                }
                else
                {
                    var olds = listOldScreens.SingleOrDefault(s => (s.ObjectKey.Equals(skey) && s.LastModifiedDate.Equals(modDate)));
                    var olds2 = listOldScreens.SingleOrDefault(s => (s.ObjectKey.Equals(skey)));
                    if (olds == null && olds2 != null)
                    {
                        difScreens.Add(screen);
                        difScreensKeys.Add(screen.ObjectKey);
                    }
                }
            }

            if (newOrAltered.Equals("new")) { Console.WriteLine("Transformed Screens:"); }
            else if (newOrAltered.Equals("altered")) { Console.WriteLine("\nAltered Screens:"); }

            foreach (GScreen screen in difScreens)
            {
                Console.WriteLine(screen);
            }

            if (newOrAltered.Equals("new"))
            {
                if (difScreensKeys.Count() != 0)
                {
                    ToggleElement(newe, difScreensKeys, "defaultfeature");
                    CreateScreenPrep(newe, difScreensKeys, "defaultfeature");
                }
            }
        }

        public void ToggleElement(IESpace espace, List<IKey> keys, String feature)
        {
            var links = espace.GetAllDescendantsOfType<GLink>().Where(s => keys.Contains(GetDestination(s).ObjectKey));
            links = InsertIfplus(espace, keys, links);
            ToggleManager manager = new ToggleManager();
            var action = manager.GetToggleValueRetrievalAction(espace);
            foreach (GLink l in links.ToList())
            {
                if (feature == "defaultfeature")
                {
                    feature = GetDestinationName(l);
                }
                var key = manager.GetToggleKey(espace.Name, feature);
                var name = manager.GetToggleName(feature);
                if (l.Parent is GParent1)
                {
                    var parent = (GParent1)l.Parent;
                    var rec = manager.CreateToggleRecord(key, name, espace);

                    EncapsulatedInIf(parent, l, espace, feature);
                }
                else if (l.Parent is GParent2)
                {
                    var parent = (GParent2)l.Parent;
                    var rec =manager.CreateToggleRecord(key, name, espace);

                    EncapsulatedInIf2(parent, l, espace, feature);
                }
                else
                {
                    Console.WriteLine($"Bypass Link {l} because parent is not IPlaceholderContentWidget or IContainerWidget. Parent is {l.Parent}");
                }
            }
        }

        protected abstract void EncapsulatedInIf(GParent1 p, GLink o, IESpace eSpace, String feature);

        protected abstract void EncapsulatedInIf2(GParent2 p, GLink o, IESpace eSpace, String feature);

        protected abstract void CreateScreenPrep(IESpace espace, List<IKey> screenskeys, String feature);

        protected abstract IObjectSignature GetDestination(GLink l);

        protected abstract String GetDestinationName(GLink l);

        protected abstract IEnumerable<GLink> InsertIfplus(IESpace espace, List<IKey> keys, IEnumerable<GLink> links);
    }
}
