using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

using ThomasJepp.SaintsRow;
using ThomasJepp.SaintsRow.AssetAssembler;
using ThomasJepp.SaintsRow.GameInstances;
using ThomasJepp.SaintsRow.Localization;
using ThomasJepp.SaintsRow.Packfiles;
using ThomasJepp.SaintsRow.Strings;

namespace ClothingImportSRTT
{
    class Program
    {
        static string tempFolder = @"D:\SR\temp";
        static Dictionary<Language, List<uint>> srivStringKeys = new Dictionary<Language, List<uint>>();
        static Dictionary<Language, Dictionary<uint, string>> newStrings = new Dictionary<Language, Dictionary<uint, string>>();
        static Dictionary<Language, Dictionary<uint, string>> srttStrings = new Dictionary<Language, Dictionary<uint, string>>();

        static List<string> srivItems = new List<string>();

        static IContainer FindContainer(IAssetAssemblerFile asm, string containerName)
        {
            string name = Path.GetFileNameWithoutExtension(containerName);

            foreach (var container in asm.Containers)
            {
                if (container.Name == name)
                    return container;
            }

            return null;
        }

        static IContainer ConvertContainer(IContainer src, IAssetAssemblerFile newAsm)
        {
            IContainer dst = newAsm.CreateContainer();
            dst.Name = src.Name;
            dst.ContainerType = src.ContainerType;
            dst.Flags = src.Flags;
            dst.PrimitiveCount = src.PrimitiveCount;
            dst.PackfileBaseOffset = src.PackfileBaseOffset;
            dst.CompressionType = 9;
            dst.StubContainerParentName = src.StubContainerParentName;
            dst.AuxData = src.AuxData;
            dst.TotalCompressedPackfileReadSize = dst.TotalCompressedPackfileReadSize;

            foreach (IPrimitive srcP in src.Primitives)
            {
                IPrimitive dstP = dst.CreatePrimitive();
                dstP.Name = srcP.Name;
                dstP.Type = srcP.Type;
                dstP.Allocator = srcP.Allocator;
                dstP.Flags = srcP.Flags;
                dstP.ExtensionIndex = srcP.ExtensionIndex;
                dstP.AllocationGroup = srcP.AllocationGroup;
                dstP.CPUSize = srcP.CPUSize;
                dstP.GPUSize = srcP.GPUSize;
                dst.Primitives.Add(dstP);
            }

            return dst;
        }

        static void LoadSRIVClothingNames(IGameInstance sriv, string xtbl)
        {
            using (Stream srivItemsStream = sriv.OpenPackfileFile(xtbl))
            {
                XDocument xml = XDocument.Load(srivItemsStream);

                var table = xml.Descendants("Table");

                foreach (var node in table.Descendants("Customization_Item"))
                {
                    string name = node.Element("Name").Value;
                    srivItems.Add(name);
                }
            }
        }

        static void LoadSRIVStringHashes(IGameInstance sriv)
        {
            var results = sriv.SearchForFiles("*.le_strings");
            foreach (var result in results)
            {
                string filename = result.Value.Filename.ToLowerInvariant();
                filename = Path.GetFileNameWithoutExtension(filename);

                string[] pieces = filename.Split('_');
                string languageCode = pieces.Last();

                Language language = LanguageUtility.GetLanguageFromCode(languageCode);

                if (!srivStringKeys.ContainsKey(language))
                    srivStringKeys.Add(language, new List<uint>());

                List<uint> keys = srivStringKeys[language];

                using (Stream s = sriv.OpenPackfileFile(result.Value.Filename, result.Value.Packfile))
                {
                    StringFile file = new StringFile(s, language, sriv);

                    foreach (var hash in file.GetHashes())
                    {
                        keys.Add(hash);
                    }
                }
            }
        }

        static void LoadSRTTStrings(IGameInstance srtt)
        {
            var results = srtt.SearchForFiles("*.le_strings");
            foreach (var result in results)
            {
                string filename = result.Value.Filename.ToLowerInvariant();
                filename = Path.GetFileNameWithoutExtension(filename);

                string[] pieces = filename.Split('_');
                string languageCode = pieces.Last();

                Language language = LanguageUtility.GetLanguageFromCode(languageCode);

                if (!srttStrings.ContainsKey(language))
                    srttStrings.Add(language, new Dictionary<uint, string>());

                Dictionary<uint, string> strings = srttStrings[language];

                using (Stream s = srtt.OpenPackfileFile(result.Value.Filename, result.Value.Packfile))
                {
                    StringFile file = new StringFile(s, language, srtt);

                    foreach (var hash in file.GetHashes())
                    {
                        if (strings.ContainsKey(hash))
                        {
                            continue;
                        }

                        strings.Add(hash, file.GetString(hash));
                    }
                }
            }
        }

        static bool ClonePackfile(IGameInstance srtt, string packfileName, string clothSimFilename, IAssetAssemblerFile srttAsm, IAssetAssemblerFile newAsm)
        {
            using (Stream srttStream = srtt.OpenPackfileFile(packfileName))
            {
                if (srttStream != null)
                {
                    IContainer srttContainer = FindContainer(srttAsm, packfileName);

                    if (srttContainer != null)
                    {
                        IContainer newContainer = ConvertContainer(srttContainer, newAsm);

                        if (clothSimFilename != null)
                        {
                            IPrimitive clothSimPrimitive = newContainer.CreatePrimitive();
                            clothSimPrimitive.Name = clothSimFilename;
                            clothSimPrimitive.Type = 47; // <PrimitiveType ID="47" Name="Pcust cloth sim" />
                            clothSimPrimitive.Allocator = 0;
                            clothSimPrimitive.Flags = 0;
                            clothSimPrimitive.ExtensionIndex = 0;
                            clothSimPrimitive.AllocationGroup = 0;
                            newContainer.Primitives.Add(clothSimPrimitive);
                            newContainer.PrimitiveCount++;
                        }

                        newAsm.Containers.Add(newContainer);

                        using (IPackfile srttPackfile = Packfile.FromStream(srttStream, true))
                        {
                            using (IPackfile srivPackfile = Packfile.FromVersion(0x0A, true))
                            {
                                srivPackfile.IsCompressed = true;
                                srivPackfile.IsCondensed = true;

                                foreach (var file in srttPackfile.Files)
                                {
                                    Stream stream = file.GetStream();
                                    srivPackfile.AddFile(stream, file.Name);
                                }

                                if (clothSimFilename != null)
                                {
                                    Stream clothSimStream = srtt.OpenPackfileFile(clothSimFilename);
                                    srivPackfile.AddFile(clothSimStream, clothSimFilename);
                                }

                                using (Stream srivStream = File.Create(Path.Combine(tempFolder, packfileName)))
                                {
                                    srivPackfile.Save(srivStream);
                                    srivPackfile.Update(newContainer);
                                }
                            }
                        }
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        static void ImportClothing(string sourceXtbl, string sourceAsm, IAssetAssemblerFile newAsm, XElement customizationItemTable)
        {
            int count = 0;

            IGameInstance srtt = GameInstance.GetFromSteamId(GameSteamID.SaintsRowTheThird);

            IAssetAssemblerFile srttAsm;
            using (Stream srttAssetAssemblerStream = srtt.OpenPackfileFile(sourceAsm))
            {
                srttAsm = AssetAssemblerFile.FromStream(srttAssetAssemblerStream);
            }

            using (Stream srttItemsStream = srtt.OpenPackfileFile(sourceXtbl))
            {
                XDocument xml = XDocument.Load(srttItemsStream);

                var table = xml.Descendants("Table");

                foreach (var node in table.Descendants("Customization_Item"))
                {
                    bool found = false;

                    string name = node.Element("Name").Value;

                    if (srivItems.Contains(name))
                        continue;

                    string stringName = node.Element("DisplayName").Value;
                    uint stringKey = Hashes.CrcVolition(stringName);

                    string newStringName = "SRTT_" + stringName;
                    uint newStringKey = Hashes.CrcVolition(newStringName);

                    node.Element("DisplayName").Value = newStringName;

                    foreach (var pair in srivStringKeys)
                    {
                        Language language = pair.Key;
                        string text = null;
                        if (srttStrings[language].ContainsKey(stringKey))
                        {
                            text = srttStrings[language][stringKey];
                        }
                        else
                        {
                            text = "[format][color:red]" + srttStrings[Language.English][stringKey] + "[/format]";
                        }

                        if (!newStrings.ContainsKey(language))
                            newStrings.Add(language, new Dictionary<uint, string>());

                        if (newStrings[language].ContainsKey(newStringKey))
                            continue;

                        newStrings[language].Add(newStringKey, "SRTT: " + text);
                    }

                    bool isDLC = false;

                    var dlcElement = node.Element("Is_DLC");

                    if (dlcElement != null)
                    {
                        string isDLCString = dlcElement.Value;

                        bool.TryParse(isDLCString, out isDLC);

                        // Remove Is_DLC element so DLC items show up in SRIV
                        dlcElement.Remove();
                    }

                    //if (isDLC)
                        //continue;

                    Console.Write("[{0}] {1}... ", count, name);

                    List<string> str2Names = new List<string>();

                    var wearOptionsNode = node.Element("Wear_Options");
                    foreach (var wearOptionNode in wearOptionsNode.Descendants("Wear_Option"))
                    {
                        var meshInformationNode = wearOptionNode.Element("Mesh_Information");
                        var maleMeshFilenameNode = meshInformationNode.Element("Male_Mesh_Filename");
                        var filenameNode = maleMeshFilenameNode.Element("Filename");
                        string maleMeshFilename = filenameNode.Value;

                        string clothSimFilename = null;
                        var clothSimFilenameNode = meshInformationNode.Element("Cloth_Sim_Filename");
                        if (clothSimFilenameNode != null)
                        {
                            filenameNode = clothSimFilenameNode.Element("Filename");
                            clothSimFilename = filenameNode.Value;
                            clothSimFilename = Path.ChangeExtension(clothSimFilename, ".sim_pc");
                        }

                        var variantNodes = node.Element("Variants").Descendants("Variant");
                        foreach (var variantNode in variantNodes)
                        {
                            var meshVariantInfoNode = variantNode.Element("Mesh_Variant_Info");
                            var variantIdNode = meshVariantInfoNode.Element("VariantID");
                            uint variantId = uint.Parse(variantIdNode.Value);
                            int crc = Hashes.CustomizationItemCrc(name, maleMeshFilename, variantId);

                            string maleStr2 = String.Format("custmesh_{0}.str2_pc", crc);
                            string femaleStr2 = String.Format("custmesh_{0}f.str2_pc", crc);

                            bool foundMale = ClonePackfile(srtt, maleStr2, clothSimFilename, srttAsm, newAsm);
                            bool foundFemale = ClonePackfile(srtt, femaleStr2, clothSimFilename, srttAsm, newAsm);

                            if (foundMale || foundFemale)
                            {
                                found = true;
                            }
                        }
                    }

                    if (found)
                    {
                        customizationItemTable.Add(node);
                        count++;
                        Console.WriteLine("done.");
                    }
                    else
                    {
                        Console.WriteLine("not found!");
                    }
                }

                Console.WriteLine("Handled {0} items...", count);
            }
        }

        static void Main(string[] args)
        {
            if (Directory.Exists(tempFolder))
                Directory.Delete(tempFolder, true);

            Directory.CreateDirectory(tempFolder);

            XDocument customizationItem = null;
            using (Stream itemsTemplateStream = File.OpenRead("customization_items.xtbl"))
            {
                customizationItem = XDocument.Load(itemsTemplateStream);
            }
            var customizationItemTable = customizationItem.Descendants("Table").First();

            IAssetAssemblerFile newAsm;
            using (Stream newAsmStream = File.OpenRead("customize_item.asm_pc"))
            {
                newAsm = AssetAssemblerFile.FromStream(newAsmStream);
            }

            IGameInstance sriv = GameInstance.GetFromSteamId(GameSteamID.SaintsRowIV);
            IGameInstance srtt = GameInstance.GetFromSteamId(GameSteamID.SaintsRowTheThird);

            LoadSRIVClothingNames(sriv, "customization_items.xtbl");
            LoadSRIVClothingNames(sriv, "dlc1_customization_items.xtbl");
            LoadSRIVClothingNames(sriv, "dlc2_customization_items.xtbl");
            LoadSRIVClothingNames(sriv, "dlc3_customization_items.xtbl");
            LoadSRIVClothingNames(sriv, "dlc4_customization_items.xtbl");
            LoadSRIVClothingNames(sriv, "dlc5_customization_items.xtbl");
            LoadSRIVClothingNames(sriv, "dlc6_customization_items.xtbl");

            LoadSRIVStringHashes(sriv);
            LoadSRTTStrings(srtt);

            ImportClothing("customization_items.xtbl", "customize_item.asm_pc", newAsm, customizationItemTable);
            ImportClothing("dlc1_customization_items.xtbl", "dlc1_customize_item.asm_pc", newAsm, customizationItemTable);
            ImportClothing("dlc2_customization_items.xtbl", "dlc2_customize_item.asm_pc", newAsm, customizationItemTable);
            ImportClothing("dlc3_customization_items.xtbl", "dlc3_customize_item.asm_pc", newAsm, customizationItemTable);

            using (Stream xtblOutStream = File.Create(Path.Combine(tempFolder, "customization_items.xtbl")))
            {
                XmlWriterSettings settings = new XmlWriterSettings();
                settings.OmitXmlDeclaration = true;
                settings.Encoding = new UTF8Encoding(false);
                settings.NewLineChars = "\r\n";
                settings.Indent = true;
                settings.IndentChars = "\t";
                using (XmlWriter writer = XmlWriter.Create(xtblOutStream, settings))
                {
                    customizationItem.Save(writer);
                }
            }

            using (Stream asmOutStream = File.Create(Path.Combine(tempFolder, "customize_item.asm_pc")))
            {
                newAsm.Save(asmOutStream);
            }

            foreach (var pair in newStrings)
            {
                Language language = pair.Key;
                Dictionary<uint, string> strings = pair.Value;

                UInt16 bucketCount = (UInt16)(strings.Count() / 5);
                if (bucketCount < 32)
                    bucketCount = 32;
                else if (bucketCount < 64)
                    bucketCount = 64;
                else if (bucketCount < 128)
                    bucketCount = 128;
                else if (bucketCount < 256)
                    bucketCount = 256;
                else if (bucketCount < 512)
                    bucketCount = 512;
                else
                    bucketCount = 1024;

                StringFile stringFile = new StringFile(bucketCount, language, sriv);

                foreach (var newString in strings)
                {
                    stringFile.AddString(newString.Key, newString.Value);
                }

                using (Stream stringsOutStream = File.Create(Path.Combine(tempFolder, String.Format("srtt_clothing_{0}.le_strings", LanguageUtility.GetLanguageCode(language).ToLowerInvariant()))))
                {
                    stringFile.Save(stringsOutStream);
                }
            }

            Console.WriteLine("Done!");
            Console.ReadLine();
        }
    }
}