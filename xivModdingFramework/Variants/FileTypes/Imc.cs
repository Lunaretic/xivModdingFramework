﻿// xivModdingFramework
// Copyright © 2018 Rafael Gonzalez - All Rights Reserved
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using xivModdingFramework.General.Enums;
using xivModdingFramework.Items;
using xivModdingFramework.Items.DataContainers;
using xivModdingFramework.Items.Enums;
using xivModdingFramework.Items.Interfaces;
using xivModdingFramework.SqPack.FileTypes;
using xivModdingFramework.Variants.DataContainers;

namespace xivModdingFramework.Variants.FileTypes
{
    public enum ImcType : short {
        Unknown = 0,
        NonSet = 1,
        Set = 31
    }

    /// <summary>
    /// This class contains the methods that deal with the .imc file type 
    /// </summary>
    public class Imc
    {
        private const string ImcExtension = ".imc";
        private readonly DirectoryInfo _gameDirectory;
        private readonly XivDataFile _dataFile;

        public Imc(DirectoryInfo gameDirectory, XivDataFile dataFile)
        {
            _gameDirectory = gameDirectory;
            _dataFile = dataFile;
        }

        /// <summary>
        /// Gets the relevant IMC information for a given item
        /// </summary>
        /// <param name="item">The item to get the version for</param>
        /// <param name="modelInfo">The model info of the item</param>
        /// <returns>The XivImc Data</returns>
        public async Task<XivImc> GetImcInfo(IItemModel item)
        {
            var info = await GetFullImcInfo(item);
            var slot = item.GetItemSlotAbbreviation();

            var result = info.GetEntry(item.ModelInfo.ImcSubsetID, slot);
            return result;
        }

        public async Task<FullImcInfo> GetFullImcInfo(IItemModel item)
        {
            FullImcInfo info = null;
            try
            {
                var imcPath = GetImcPath(item);
                var path = imcPath.Folder + "/" + imcPath.File;
                info = await GetFullImcInfo(path);
            } catch
            {
                // Some dual wield items don't have a second IMC, and just default to the first.
                var gear = (XivGear)item;
                if (gear != null && gear.PairedItem != null)
                {
                    var pair = gear.PairedItem;
                    var imcPath = GetImcPath(pair);
                    var path = imcPath.Folder + "/" + imcPath.File;
                    return await (GetFullImcInfo(path));
                }
            }

            return info;
        }


        /// <summary>
        /// Gets the full IMC information for a given item
        /// </summary>
        /// <param name="item"></param>
        /// <param name="useSecondary">Determines if the SecondaryModelInfo should be used instead.(XivGear only)</param>
        /// <returns>The ImcData data</returns>
        public async Task<FullImcInfo> GetFullImcInfo(string path)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);


            var imcOffset = await index.GetDataOffset(path);

            if (imcOffset == 0)
            {
                throw new InvalidDataException($"Could not find offset for {path}");
            }

            var imcByteData = await dat.GetType2Data(imcOffset, _dataFile);

            return await Task.Run(() =>
            {
                using (var br = new BinaryReader(new MemoryStream(imcByteData)))
                {
                    var subsetCount = br.ReadInt16();
                    var identifier = br.ReadInt16();
                    var imcData = new FullImcInfo()
                    {
                        TypeIdentifier = (ImcType) identifier,
                        DefaultSubset = new List<XivImc>(),
                        SubsetList = new List<List<XivImc>>(subsetCount)
                    };

                    //weapons and monsters do not have variant sets
                    if (imcData.TypeIdentifier == ImcType.NonSet)
                    {
                        // This type uses the first short for both Variant and VFX.
                        byte variant = br.ReadByte();
                        byte unknown = br.ReadByte();
                        ushort mask = br.ReadUInt16();
                        ushort vfx = br.ReadUInt16();

                        imcData.DefaultSubset.Add(new XivImc
                        {
                            Variant = variant,
                            Unknown = unknown,
                            Mask = mask,
                            Vfx = variant
                        });

                        for (var i = 0; i < subsetCount; i++)
                        {
                            variant = br.ReadByte();
                            unknown = br.ReadByte();
                            mask = br.ReadUInt16();
                            vfx = br.ReadUInt16();

                            var newEntry = new XivImc
                            {
                                Variant = variant,
                                Unknown = unknown,
                                Mask = mask,
                                Vfx = vfx
                            };
                            var subset = new List<XivImc>() { newEntry };
                            imcData.SubsetList.Add(subset);
                        }
                    }
                    else if(imcData.TypeIdentifier == ImcType.Set)
                    {
                        // Identifier used by Equipment.
                        imcData.DefaultSubset = new List<XivImc>()
                        {
                            new XivImc
                                {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            new XivImc
                                {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                        };

                        for (var i = 0; i < subsetCount; i++)
                        {
                            // gets the data for each slot in the current variant set
                            var imcGear = new List<XivImc>()
                            {
                                new XivImc
                                    {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                                new XivImc
                                    {Variant = br.ReadByte(), Unknown = br.ReadByte(), Mask = br.ReadUInt16(), Vfx = br.ReadUInt16()},
                            };
                            imcData.SubsetList.Add(imcGear);
                        }
                    } else
                    {
                        throw new NotSupportedException("Unknown IMC Type Identifier. (Please report this item in the TexTools Discord #bug_reports channel.)");
                    }

                    return imcData;
                }
            });
        }

        public async Task SaveImcInfo(XivImc info, IItemModel item)
        {
            var full = await GetFullImcInfo(item);
            full.SetEntry(info, item.ModelInfo.ImcSubsetID, item.GetItemSlotAbbreviation());
            await SaveFullImcInfo(full, item);
        }

        public async Task SaveImcInfo(XivImc info, string path, int subsetId = -1, string slot = "")
        {
            var full = await GetFullImcInfo(path);
            full.SetEntry(info, subsetId, slot);
            await SaveFullImcInfo(full, path);
        }

        public async Task SaveFullImcInfo(FullImcInfo info, IItemModel item)
        {
            try
            {
                var imcPath = GetImcPath(item);
                var path = imcPath.Folder + "/" + imcPath.File;
                await SaveFullImcInfo(info, path);
            }
            catch
            {
                // Some dual wield items don't have a second IMC, and just default to the first.
                var gear = (XivGear)item;
                if (gear != null && gear.PairedItem != null)
                {
                    var pair = gear.PairedItem;
                    var imcPath = GetImcPath(pair);
                    var path = imcPath.Folder + "/" + imcPath.File;
                    await (SaveFullImcInfo(info, path));
                }
            }
            return;

        }

        public async Task SaveFullImcInfo(FullImcInfo info, string path, string itemName = null, string category = null, string source = null)
        {
            var index = new Index(_gameDirectory);
            var dat = new Dat(_gameDirectory);


            var imcOffset = await index.GetDataOffset(path);

            // No writing new IMC files.
            if (imcOffset == 0)
            {
                throw new InvalidDataException($"Could not find offset for {path}");
            }

            var data = new List<byte>();

            // 4 Header bytes.
            data.AddRange(BitConverter.GetBytes((short) info.SubsetCount));
            data.AddRange(BitConverter.GetBytes((short) info.TypeIdentifier));

            // The rest of this is easy, it's literally just post all the sets in order.
            foreach(var entry in info.DefaultSubset)
            {
                data.AddRange(entry.GetBytes(info.TypeIdentifier));
            }

            foreach(var set in info.SubsetList)
            {
                foreach (var entry in set)
                {
                    data.AddRange(entry.GetBytes(info.TypeIdentifier));
                }
            }

            // That's it.

            itemName ??= Path.GetFileName(path);
            category ??= "Meta";
            source ??= "Internal";

            await dat.ImportType2Data(data.ToArray(), itemName, path, category, source);
        }

        /// <summary>
        /// Gets the IMC internal path for the given model info
        /// </summary>
        /// <param name="modelInfo">The model info of the item</param>
        /// <param name="itemType">The type of the item</param>
        /// <returns>A touple containing the Folder and File strings</returns>
        private static (string Folder, string File) GetImcPath(IItemModel item)
        {
            string imcFolder = item.GetItemRootFolder();
            string imcFile;

            var primaryId = item.ModelInfo.PrimaryID.ToString().PadLeft(4, '0');
            var secondaryId = item.ModelInfo.SecondaryID.ToString().PadLeft(4, '0');
            var itemType = item.GetPrimaryItemType();

            switch (itemType)
            {
                case XivItemType.equipment:
                    imcFile = $"e{primaryId}{ImcExtension}";
                    break;
                case XivItemType.accessory:
                    imcFile = $"a{primaryId}{ImcExtension}";
                    break;
                case XivItemType.weapon:
                    imcFile = $"b{secondaryId}{ImcExtension}";
                    break;
                case XivItemType.monster:
                    imcFile = $"b{secondaryId}{ImcExtension}";
                    break;
                case XivItemType.demihuman:
                    imcFile = $"e{secondaryId}{ImcExtension}";
                    break;
                default:
                    imcFolder = "";
                    imcFile = "";
                    break;
            }

            return (imcFolder, imcFile);
        }

        /// <summary>
        /// A dictionary containing slot offset data in format [Slot Abbreviation, Offset within variant set]
        /// </summary>
        private static readonly Dictionary<string, int> _slotOffsetDictionary = new Dictionary<string, int>
        {
            {"met", 0},
            {"top", 1},
            {"glv", 2},
            {"dwn", 3},
            {"sho", 4},
            {"ear", 0},
            {"nek", 1},
            {"wrs", 2},
            {"rir", 3},
            {"ril", 4}
        };

        /// <summary>
        /// Class containing the information for and IMC file
        /// </summary>
        public class FullImcInfo
        {
            /// <summary>
            /// Get the number of subsets.
            ///  -NOT- the same as number of material variants.
            /// </summary>
            public int SubsetCount { get
                {
                    return SubsetList.Count;
                }
                set {
                    throw new NotSupportedException("Attempted to directly set SubsetCount.");
                }
            }

            /// <summary>
            /// Get the size of each subset (Either 1 or 5)
            /// </summary>
            public int SubsetSize
            {
                get
                {
                    return DefaultSubset.Count;
                }
                set
                {
                    throw new NotSupportedException("Attempted to directly set SubsetSize.");
                }
            }

            /// <summary>
            /// Unknown Value
            /// </summary>
            public ImcType TypeIdentifier { get; set; }

            /// <summary>
            /// Total # of Gear Subsets.
            /// NOT the same as number of material variants.
            /// IItemModel->ImcSubsetID can be used as an index accessory in this list.
            /// </summary>
            public List<List<XivImc>> SubsetList { get; set; }

            /// <summary>
            /// The default variant set for the item, always the variant immediatly following the header
            /// </summary>
            public List<XivImc> DefaultSubset { get; set; }


            /// <summary>
            /// Retrieve a given IMC info. Negative values retrieve the default set.
            /// </summary>
            /// <param name="index">IMC Variant/Subset ID</param>
            /// <param name="slot">Slot Abbreviation</param>
            /// <returns></returns>
            public XivImc GetEntry(int subsetID = -1, string slot = "")
            {
                // Variant IDs are 1 based, not 0 based.
                var index = subsetID - 1;

                // Invalid Index, return default.
                if (index >= SubsetCount || index < 0)
                {
                    index = -1;
                }

                // Test for getting default set.
                var subset = DefaultSubset;
                if(index >= 0)
                {
                    subset = SubsetList[index];
                }

                // Get which offset the slot uses.
                var idx = 0;
                if(_slotOffsetDictionary.ContainsKey(slot) && _slotOffsetDictionary[slot] < subset.Count)
                {
                    idx = _slotOffsetDictionary[slot];
                }

                return subset[idx];
            }

            public void SetEntry(XivImc info, int subsetID = -1, string slot = "")
            {
                // Variant IDs are 1 based, not 0 based.
                var index = subsetID - 1;

                // Invalid Index, return default.
                if (index >= SubsetCount || index < 0)
                {
                    index = -1;
                }

                // Test for getting default set.
                var subset = DefaultSubset;
                if (index >= 0)
                {
                    subset = SubsetList[index];
                }

                // Get which offset the slot uses.
                var idx = 0;
                if (_slotOffsetDictionary.ContainsKey(slot) && _slotOffsetDictionary[slot] < subset.Count)
                {
                    idx = _slotOffsetDictionary[slot];
                }

                // Assign info.
                subset[idx] = info;
            }
        }

    }
}