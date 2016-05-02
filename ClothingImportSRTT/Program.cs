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
using ThomasJepp.SaintsRow.Packfiles;

namespace ClothingImportSRTT
{
    class Program
    {
        static string tempFolder = @"D:\SR\temp";

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

        static void ImportClothing(string sourceXtbl, string sourceAsm, IAssetAssemblerFile newAsm, XElement customizationItemTable)
        {
            List<string> srivItems = new List<string>();

            IGameInstance sriv = GameInstance.GetFromSteamId(GameSteamID.SaintsRowIV);
            using (Stream srivItemsStream = sriv.OpenPackfileFile("customization_items.xtbl"))
            {
                XDocument xml = XDocument.Load(srivItemsStream);

                var table = xml.Descendants("Table");

                foreach (var node in table.Descendants("Customization_Item"))
                {
                    string name = node.Element("Name").Value;
                    srivItems.Add(name);
                }
            }

            int count = 0;

            IGameInstance srtt = GameInstance.GetFromSteamId(GameSteamID.SaintsRowTheThird);

            IAssetAssemblerFile srttAsm;
            using (Stream srttAssetAssemblerStream = srtt.OpenPackfileFile(sourceAsm))
            {
                srttAsm = AssetAssemblerFile.FromStream(srttAssetAssemblerStream);
            }

            using (Stream srttItemsStream = srtt.OpenPackfileFile(sourceXtbl))
            {
                bool found = false;

                XDocument xml = XDocument.Load(srttItemsStream);

                var table = xml.Descendants("Table");

                foreach (var node in table.Descendants("Customization_Item"))
                {
                    string name = node.Element("Name").Value;

                    if (srivItems.Contains(name))
                        continue;

                    bool isDLC = false;

                    var dlcElement = node.Element("Is_DLC");

                    if (dlcElement != null)
                    {
                        string isDLCString = dlcElement.Value;

                        bool.TryParse(isDLCString, out isDLC);
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

                            using (Stream srttMaleStream = srtt.OpenPackfileFile(maleStr2))
                            {
                                if (srttMaleStream != null)
                                {
                                    IContainer srttContainer = FindContainer(srttAsm, maleStr2);

                                    if (srttContainer != null)
                                    {
                                        found = true;

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

                                        using (IPackfile srttMalePackfile = Packfile.FromStream(srttMaleStream, true))
                                        {
                                            using (IPackfile srivMalePackfile = Packfile.FromVersion(0x0A, true))
                                            {
                                                srivMalePackfile.IsCompressed = true;
                                                srivMalePackfile.IsCondensed = true;

                                                foreach (var file in srttMalePackfile.Files)
                                                {
                                                    Stream stream = file.GetStream();
                                                    srivMalePackfile.AddFile(stream, file.Name);
                                                }

                                                if (clothSimFilename != null)
                                                {
                                                    Stream clothSimStream = srtt.OpenPackfileFile(clothSimFilename);
                                                    srivMalePackfile.AddFile(clothSimStream, clothSimFilename);
                                                }

                                                using (Stream srivMaleStream = File.Create(Path.Combine(tempFolder, maleStr2)))
                                                {
                                                    srivMalePackfile.Save(srivMaleStream);
                                                    srivMalePackfile.Update(newContainer);
                                                }
                                            }
                                        }
                                    }
                                }
                            }


                            using (Stream srttFemaleStream = srtt.OpenPackfileFile(femaleStr2))
                            {
                                if (srttFemaleStream != null)
                                {
                                    IContainer srttContainer = FindContainer(srttAsm, femaleStr2);

                                    if (srttContainer != null)
                                    {
                                        found = true;

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

                                        using (IPackfile srttFemalePackfile = Packfile.FromStream(srttFemaleStream, true))
                                        {

                                            using (IPackfile srivFemalePackfile = Packfile.FromVersion(0x0A, true))
                                            {
                                                srivFemalePackfile.IsCompressed = true;
                                                srivFemalePackfile.IsCondensed = true;

                                                foreach (var file in srttFemalePackfile.Files)
                                                {
                                                    Stream stream = file.GetStream();
                                                    srivFemalePackfile.AddFile(stream, file.Name);
                                                }

                                                if (clothSimFilename != null)
                                                {
                                                    Stream clothSimStream = srtt.OpenPackfileFile(clothSimFilename);
                                                    srivFemalePackfile.AddFile(clothSimStream, clothSimFilename);
                                                }

                                                using (Stream srivFemaleStream = File.Create(Path.Combine(tempFolder, femaleStr2)))
                                                {
                                                    srivFemalePackfile.Save(srivFemaleStream);
                                                    srivFemalePackfile.Update(newContainer);
                                                }
                                            }
                                        }
                                    }
                                }
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


            Console.WriteLine("Done!");
            Console.ReadLine();
        }
    }
}