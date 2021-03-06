#region License
/* Copyright (c) 2008, Katharine Berry
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of Katharine Berry nor the names of any contributors
 *       may be used to endorse or promote products derived from this software
 *       without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY KATHARINE BERRY ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL KATHARINE BERRY BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 ******************************************************************************/
#endregion
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using libsecondlife;
using libsecondlife.Packets;
using Newtonsoft.Json;
using System.Diagnostics;
using AjaxLife.Converters;
using Affirma.ThreeSharp;
using Affirma.ThreeSharp.Query;

namespace AjaxLife
{
    public class Events
    {
        // Fields
        private Queue<Hashtable> pending = new Queue<Hashtable>();
        private bool active = true;
        private User user;
        private SecondLife Client;
        private List<LLUUID> LoadedInventory = new List<LLUUID>();

        // Constructor. Lets us have access to various useful user things.
        public Events(User user)
        {
            this.Client = user.Client;
            this.user = user;
        }
        
        // Returns the number of events pending.
        public int GetEventCount()
        {
            return this.pending.Count;
        }

        // Returns the event queue in JSON format.
        public string GetPendingJson(SecondLife client)
        {
            enqueue(this.GetFooter(client));
            return MakeJson.FromHashtableQueue(this.pending);
        }
        
        
        // Marks us as inactive - i.e. we should be logged off.
        public void deactivate()
        {
            active = false;
        }

        // Clear the inventory cache. This is needed to avoid confusing the client if it
        // reloads without relogging.
        public void ClearInventory()
        {
            LoadedInventory.Clear();
        }

        // Whatever this function returns is tacked onto every message queue response.
        // It contains useful data (thus the name "UsefulData") - that is, the user's position,
        // region, and the positions of others in the region.
        public Hashtable GetFooter(SecondLife client)
        {
            Hashtable message = new Hashtable();
            message.Add("MessageType", "UsefulData");
            message.Add("Positions", client.Network.CurrentSim.AvatarPositions);
            message.Add("YourPosition", client.Self.SimPosition);
            message.Add("YourRegion", client.Network.CurrentSim.Name);
            return message;
        }
        
        private void enqueue(Hashtable message)
        {
            if(!message.ContainsKey("MessageType")) return;
            if(user.RequestedEvents == null || user.RequestedEvents.Contains((string)message["MessageType"]))
            {
                this.pending.Enqueue(message);
            }
        }

        // CALLBACKS
        
        // These are all assigned to LibSL callbacks in Connect.cs. This determines their argument order.
        // The if(!active) check is to ensure they don't get called after we've logged off. This is a
        // LibSL bug.
        
        // These almost all perform the same task:
        // 1) Create a hashtable
        // 2) Place various passed-in arguments in the hashtable
        // 3) Optionally, loop through one of the arguments if necessary, and add this to the hashtable
        //    as a bunch more hashtables.
        // 4) Enqueue the hashtable in the message queue. This is periodically emptied by the client.
        
        public void Avatars_OnAvatarGroups(LLUUID avatarID, List<AvatarGroup> groups)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "AvatarGroups");
            item.Add("AvatarID", avatarID);
            item.Add("Groups", groups);
            enqueue(item);
        }

        public void Avatars_OnAvatarInterests(LLUUID avatarID, Avatar.Interests interests)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "AvatarInterests");
            item.Add("AvatarID", avatarID);
            item.Add("WantToMask", interests.WantToMask);
            item.Add("WantToText", interests.WantToText);
            item.Add("SkillsMask", interests.SkillsMask);
            item.Add("SkillsText", interests.SkillsText);
            item.Add("LanguagesText", interests.LanguagesText);
            enqueue(item);
        }

        public void Avatars_OnAvatarNames(Dictionary<LLUUID, string> names)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "AvatarNames");
            item.Add("Names", names);
            enqueue(item);
        }

        public void Avatars_OnAvatarProperties(Packet packet, Simulator sim)
        {
            Hashtable item = new Hashtable();
            AvatarPropertiesReplyPacket reply = (AvatarPropertiesReplyPacket)packet;
            item.Add("MessageType",     "AvatarProperties");
            item.Add("AvatarID",        reply.AgentData.AvatarID);
            item.Add("PartnerID",       reply.PropertiesData.PartnerID);
            item.Add("AboutText",       Helpers.FieldToUTF8String(reply.PropertiesData.AboutText));
            item.Add("FirstLifeText",   Helpers.FieldToUTF8String(reply.PropertiesData.FLAboutText));
            item.Add("FirstLifeImage",  reply.PropertiesData.FLImageID);
            item.Add("ProfileImage",    reply.PropertiesData.ImageID);
            item.Add("ProfileURL",      Helpers.FieldToUTF8String(reply.PropertiesData.ProfileURL));
            item.Add("BornOn",          Helpers.FieldToUTF8String(reply.PropertiesData.BornOn));
            item.Add("CharterMember",   Helpers.FieldToUTF8String(reply.PropertiesData.CharterMember));
            item.Add("AllowPublish",    reply.PropertiesData.Flags & (0x1 << 0));
            item.Add("MaturePublish",   reply.PropertiesData.Flags & (0x1 << 1));
            item.Add("Identified",      reply.PropertiesData.Flags & (0x1 << 2));
            item.Add("Transacted",      reply.PropertiesData.Flags & (0x1 << 3));
            item.Add("Online",          reply.PropertiesData.Flags & (0x1 << 4));
            enqueue(item);
        }

        public void Avatars_OnFriendNotification(LLUUID agentID, bool online)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "FriendNotification");
            item.Add("AgentID", agentID);
            item.Add("Online", online);
            enqueue(item);
        }

        public void Friends_OnFriendFound(LLUUID agentID, ulong regionHandle, LLVector3 location)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "FriendFound");
            item.Add("Location", location);
            item.Add("RegionHandle", regionHandle.ToString()); // String to avoid upsetting JavaScript.
            enqueue(item);
        }

        public void Directory_OnDirPeopleReply(LLUUID queryID, List<DirectoryManager.AgentSearchData> matchedPeople)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "DirPeopleReply");
            List<Hashtable> results = new List<Hashtable>();
            foreach (DirectoryManager.AgentSearchData person in matchedPeople)
            {
                Hashtable result = new Hashtable();
                result.Add("AgentID", person.AgentID);
                result.Add("FirstName", person.FirstName);
                result.Add("LastName", person.LastName);
                results.Add(result);
            }
            item.Add("Results", results);
            enqueue(item);
        }

        public void Directory_OnDirGroupsReply(LLUUID queryID, List<DirectoryManager.GroupSearchData> matchedGroups)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "DirGroupsReply");
            List<Hashtable> results = new List<Hashtable>();
            foreach (DirectoryManager.GroupSearchData group in matchedGroups)
            {
                Hashtable result = new Hashtable();
                result.Add("GroupID", group.GroupID);
                result.Add("Name", group.GroupName);
                result.Add("MemberCount", group.Members);
                results.Add(result);
            }
            item.Add("Results", results);
            enqueue(item);
        }

        public bool Inventory_OnObjectOffered(InstantMessage offerDetails, AssetType type, LLUUID objectID, bool fromTask)
        {
            if (!active) return false;
            Hashtable hashtable = new Hashtable();
            hashtable.Add("MessageType", "ObjectOffered");
            hashtable.Add("FromAgentID", offerDetails.FromAgentID);
            hashtable.Add("FromAgentName", offerDetails.FromAgentName);
            hashtable.Add("RegionID", offerDetails.RegionID);
            hashtable.Add("Position", offerDetails.Position);
            hashtable.Add("Timestamp", offerDetails.Timestamp);
            hashtable.Add("Type", type);
            hashtable.Add("ObjectID", objectID);
            hashtable.Add("FromTask", fromTask);
            enqueue(hashtable);
            return true; // Sigh...
        }

        public void Network_OnDisconnected(NetworkManager.DisconnectType reason, string message)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "Disconnected");
            item.Add("Reason", reason);
            item.Add("Message", message);
            enqueue(item);
        }

        public void Self_OnBalanceUpdated(int balance)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "BalanceUpdated");
            item.Add("Balance", balance);
            enqueue(item);
        }
        public void Self_OnChat(string message, ChatAudibleLevel audible, ChatType type, ChatSourceType sourceType, string fromName, LLUUID id, LLUUID ownerid, LLVector3 position)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "SpatialChat");
            item.Add("Message", message);
            item.Add("Audible", audible);
            item.Add("Type", type);
            item.Add("SourceType", sourceType);
            item.Add("FromName", fromName);
            item.Add("ID", id);
            item.Add("OwnerID", ownerid);
            item.Add("Position", position);
            enqueue(item);
        }
        public void Self_OnInstantMessage(InstantMessage im, Simulator simulator)
        {
            LLUUID fromAgentID = im.FromAgentID;
            string fromAgentName = im.FromAgentName;
            //LLUUID toAgentID = im.ToAgentID;
            LLUUID regionID = im.RegionID;
            LLVector3 position = im.Position;
            InstantMessageDialog dialog = im.Dialog;
            bool groupIM = im.GroupIM;
            LLUUID imSessionID = im.IMSessionID;
            DateTime timestamp = im.Timestamp;
            string message = im.Message;
            InstantMessageOnline offline = im.Offline;
            byte[] binaryBucket = im.BinaryBucket;
            uint parentEstateID = im.ParentEstateID;
            Hashtable item = new Hashtable();
            item.Add("MessageType", "InstantMessage");
            item.Add("FromAgentID", fromAgentID);
            item.Add("FromAgentName", fromAgentName);
            item.Add("ParentEstateID", parentEstateID.ToString());
            item.Add("RegionID", regionID);
            item.Add("Position", position);
            item.Add("Dialog", dialog);
            item.Add("GroupIM", groupIM);
            item.Add("IMSessionID", imSessionID);
            item.Add("Timestamp", timestamp);
            item.Add("Message", message);
            item.Add("Offline", offline);
            item.Add("BinaryBucket", binaryBucket);
            enqueue(item);
        }

        public void Self_OnMoneyBalanceReplyReceived(LLUUID transactionID, bool transactionSuccess, int balance, int metersCredit, int metersCommitted, string description)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "MoneyBalanceReplyReceived");
            item.Add("TransactionID", transactionID);
            item.Add("TransactionSuccess", transactionSuccess);
            item.Add("Balance", balance);
            item.Add("MetersCredit", metersCredit);
            item.Add("MetersCommitted", metersCommitted);
            item.Add("Description", description);
            enqueue(item);
        }

        public void Self_OnScriptDialog(string message, string objectName, LLUUID imageID, LLUUID objectID, string firstName, string lastName, int chatChannel, List<string> buttons)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "ScriptDialog");
            item.Add("Message", message);
            item.Add("ObjectName", objectName);
            item.Add("ImageID", imageID);
            item.Add("ObjectID", objectID);
            item.Add("FirstName", firstName);
            item.Add("LastName", lastName);
            item.Add("ChatChannel", chatChannel);
            item.Add("Buttons", buttons);
            enqueue(item);
        }

        public void Self_OnScriptQuestion(Simulator simulator, LLUUID taskID, LLUUID itemID, string objectName, string objectOwner, ScriptPermission questions)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "ScriptPermissionRequest");
            item.Add("TaskID", taskID);
            item.Add("ItemID", itemID);
            item.Add("ObjectName", objectName);
            item.Add("ObjectOwner", objectOwner);
            item.Add("Permissions", (int)questions);
            enqueue(item);
        }

        public void Self_OnTeleport(string message, AgentManager.TeleportStatus status, AgentManager.TeleportFlags flags)
        {
            Hashtable item = new Hashtable();
            item.Add("MessageType", "Teleport");
            item.Add("Status", status);
            item.Add("Flags", flags);
            if (status == AgentManager.TeleportStatus.Finished)
            {
                Client.Self.Movement.Camera.SetPositionOrientation(new LLVector3(128, 128, 0), 0, 0, 0);
            }
            enqueue(item);
        }

        public void Packet_MapBlockReply(Packet packet, Simulator sim)
        {
            MapBlockReplyPacket reply = (MapBlockReplyPacket)packet;
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "MapBlocks");
            Hashtable blocks = new Hashtable();
            float temp1, temp2; // Neither of these do anything, really. Just to make Helpers.GlobalPosToRegionHandle happy.
            foreach (MapBlockReplyPacket.DataBlock data in reply.Data)
            {
                string name = Helpers.FieldToUTF8String(data.Name);
                if (blocks.ContainsKey(name.ToLowerInvariant()))
                {
                    continue;
                }
                Hashtable simhash = new Hashtable();
                simhash.Add("Name", Helpers.FieldToUTF8String(data.Name));
                simhash.Add("Access", data.Access);
                simhash.Add("Agents", data.Agents);
                simhash.Add("X", data.X);
                simhash.Add("Y", data.Y);
                simhash.Add("Flags", data.RegionFlags);
                // Convert the regionhandle to a string - JavaScript is likely to get upset over long integers.
                simhash.Add("RegionHandle", Helpers.GlobalPosToRegionHandle(data.X*256, data.Y*256, out temp1, out temp2).ToString());
                blocks.Add(Helpers.FieldToUTF8String(data.Name).ToLowerInvariant(), simhash);
            }
            hash.Add("Blocks", blocks);
            enqueue(hash);
        }

        public void Packet_MapItemReply(Packet packet, Simulator sim)
        {
            MapItemReplyPacket reply = (MapItemReplyPacket)packet;
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "MapItems");
            List<Hashtable> items = new List<Hashtable>();
            hash.Add("ItemType", reply.RequestData.ItemType);
            foreach (MapItemReplyPacket.DataBlock data in reply.Data)
            {
                Hashtable itemhash = new Hashtable();
                itemhash.Add("Name", Helpers.FieldToUTF8String(data.Name));
                itemhash.Add("X", data.X);
                itemhash.Add("Y", data.Y);
                itemhash.Add("Extra", data.Extra);
                itemhash.Add("Extra2", data.Extra2);
                itemhash.Add("ID", data.ID);
                items.Add(itemhash);
            }
            hash.Add("Items", items);
            enqueue(hash);
        }

        public void Assets_OnImageReceived(ImageDownload image, AssetTexture asset)
        {
            if (image.NotFound)
            {
                Console.WriteLine("Failed to download " + image.ID + " - not found.");
                Hashtable hash = new Hashtable();
                hash.Add("MessageType", "ImageDownloaded");
                hash.Add("UUID", image.ID);
                hash.Add("Success", false);
                hash.Add("Error", "Image not found in database.");
                enqueue(hash);
            }
            else if (image.Success)
            {
                bool success = true;
                string key = image.ID.ToString();
                try
                {
                    libsecondlife.Imaging.ManagedImage decoded;
                    libsecondlife.Imaging.OpenJPEG.DecodeToImage(image.AssetData, out decoded);
                    byte[] img = decoded.ExportTGA();
                    decoded.Clear();
                    File.WriteAllBytes(AjaxLife.TEXTURE_CACHE + key + ".tga", img);
                    Process process = Process.Start("convert", AjaxLife.TEXTURE_CACHE + key + ".tga " + AjaxLife.TEXTURE_CACHE + key + ".png");
                    process.WaitForExit();
                    process.Dispose();
                    File.Delete(AjaxLife.TEXTURE_CACHE + key + ".tga");
                    Console.WriteLine("Downloaded image " + key + " - " + image.Size + " bytes.");
                    if(AjaxLife.USE_S3)
                    {
                        try
                        {
                            IThreeSharp service = new ThreeSharpQuery(AjaxLife.S3Config);
                            Affirma.ThreeSharp.Model.ObjectAddRequest request = new Affirma.ThreeSharp.Model.ObjectAddRequest(AjaxLife.TEXTURE_BUCKET, key + ".png");
                            request.LoadStreamWithFile(AjaxLife.TEXTURE_CACHE + key + ".png");
                            request.Headers.Add("x-amz-acl", "public-read");
                            service.ObjectAdd(request).DataStream.Close();
                            AjaxLife.CachedTextures.Add(image.ID);
                        }
                        catch
                        {
                            success = false;
                        }
                        File.Delete(AjaxLife.TEXTURE_CACHE + key + ".png");
                    }
                }
                catch(Exception e)
                {
                    success = false;
                    AjaxLife.Debug("Events", "Texture download for "+key+" failed: "+e.Message);
                }
                Hashtable hash = new Hashtable();
                hash.Add("MessageType", "ImageDownloaded");
                hash.Add("Success", success);
                hash.Add("Size", image.Size);
                hash.Add("UUID", key);
                hash.Add("URL", AjaxLife.TEXTURE_ROOT + key + ".png");
                enqueue(hash);
            }
            else
            {
                Console.WriteLine("Failed to download " + image.ID + ".");
                Hashtable hash = new Hashtable();
                hash.Add("MessageType", "ImageDownloaded");
                hash.Add("UUID", image.ID);
                hash.Add("Success", false);
                hash.Add("Error", "Unknown error.");
                enqueue(hash);
            }
        }

        public void Friends_OnFriendshipOffered(LLUUID agentID, string agentName, LLUUID imSessionID)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "FriendshipOffered");
            hash.Add("AgentID", agentID);
            hash.Add("AgentName", agentName);
            hash.Add("IMSessionID", imSessionID);
            enqueue(hash);
        }

        public void Friends_OnFriendRights(FriendInfo friend)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "FriendRightsChanged");
            hash.Add("Name", friend.Name);
            hash.Add("ID", friend.UUID);
            hash.Add("TheirRights", friend.TheirFriendRights);
            hash.Add("MyRights", friend.MyFriendRights);
            hash.Add("Online", friend.IsOnline);
            enqueue(hash);
        }

        public void Friends_OnOnOffline(FriendInfo friend)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "FriendOnOffline");
            hash.Add("Name", friend.Name);
            hash.Add("ID", friend.UUID);
            hash.Add("TheirRights", friend.TheirFriendRights);
            hash.Add("MyRights", friend.MyFriendRights);
            hash.Add("Online", friend.IsOnline);
            enqueue(hash);
        }

        public void AvatarTracker_OnAvatarAdded(Avatar avatar)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "AvatarAdded");
            hash.Add("Name", avatar.Name);
            hash.Add("ID", avatar.ID);
            hash.Add("LocalID", avatar.LocalID);
            hash.Add("Position", avatar.Position);
            hash.Add("Scale", avatar.Scale);
            hash.Add("GroupName", avatar.GroupName);
            enqueue(hash);
        }

        public void AvatarTracker_OnAvatarRemoved(Avatar avatar)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "AvatarRemoved");
            hash.Add("Name", avatar.Name);
            hash.Add("ID", avatar.ID);
            hash.Add("LocalID", avatar.LocalID);
            enqueue(hash);
        }

        public void AvatarTracker_OnAvatarUpdated(Avatar avatar)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "AvatarUpdated");
            hash.Add("ID", avatar.ID);
            hash.Add("Position", avatar.Position);
            enqueue(hash);
        }

        public void Assets_OnAssetReceived(AssetDownload transfer, Asset asset)
        {
            if (asset == null && transfer != null)
            {
                Hashtable hash = new Hashtable();
                hash.Add("MessageType", "AssetReceived");
                hash.Add("Success", false);
                if (transfer != null)
                {
                    hash.Add("TransferID", transfer.ID);
                    hash.Add("AssetID", transfer.AssetID);
                    hash.Add("Error", transfer.Status.ToString());
                    hash.Add("AssetType", transfer.AssetType);
                }
                enqueue(hash);
                return;
            }
            if (transfer == null)
            {
                Hashtable hash = new Hashtable();
                hash.Add("MessageType", "NullTransfer");
                enqueue(hash);
                return;
            }
            try
            {
                Hashtable hash = new Hashtable();
                hash.Add("MessageType", "AssetReceived");
                hash.Add("Success", transfer.Success);
                if (!transfer.Success)
                {
                    hash.Add("AssetData", "Could not download asset: " + transfer.Status.ToString());
                }
                else
                {
                    switch (asset.AssetType)
                    {
                        case AssetType.Notecard:
                        case AssetType.LSLText:
                            hash.Add("AssetData", Helpers.FieldToUTF8String(asset.AssetData));
                            break;
                        case AssetType.Bodypart:
                            {
                                AssetBodypart part = (AssetBodypart)asset;
                                hash.Add("Creator", part.Creator);
                                hash.Add("Description", part.Description);
                                hash.Add("Textures", part.Textures);
                                hash.Add("Params", part.Params);
                                hash.Add("Permissions", part.Permissions);
                                hash.Add("Owner", part.Owner);
                            }
                            break;
                    }
                }
                hash.Add("AssetType", transfer.AssetType);
                hash.Add("AssetID", transfer.AssetID);
                hash.Add("TransferID", transfer.ID);
                enqueue(hash);
            }
            catch { }
        }

        public void Inventory_OnFolderUpdated(LLUUID folderID)
        {
            List<InventoryBase> contents = Client.Inventory.Store.GetContents(folderID);
            Hashtable roothash = new Hashtable();
            roothash.Add("MessageType", "FolderUpdated");
            roothash.Add("FolderID", folderID);
            List<Hashtable> response = new List<Hashtable>();
            lock (LoadedInventory)
            {
                foreach (InventoryBase o in contents)
                {
                    if (LoadedInventory.Contains(o.UUID)) continue;
                    LoadedInventory.Add(o.UUID);
                    Hashtable hash = new Hashtable();
                    if (o is InventoryFolder)
                    {
                        InventoryFolder folder = (InventoryFolder)o;
                        hash.Add("Type", "InventoryFolder");
                        hash.Add("Name", folder.Name);
                        hash.Add("PreferredType", folder.PreferredType);
                        hash.Add("OwnerID", folder.OwnerID);
                        hash.Add("UUID", folder.UUID);
                        response.Add(hash);
                    }
                    else if (o is InventoryItem)
                    {
                        InventoryItem item = (InventoryItem)o;
                        hash.Add("Type", "InventoryItem");
                        hash.Add("Name", item.Name);
                        hash.Add("UUID", item.UUID);
                        hash.Add("AssetType", item.AssetType);
                        hash.Add("AssetUUID", item.AssetUUID);
                        hash.Add("CreatorID", item.CreatorID);
                        hash.Add("OwnerID", item.OwnerID);
                        hash.Add("CreationDate", item.CreationDate);
                        hash.Add("Description", item.Description);
                        hash.Add("Flags", item.Flags);
                        hash.Add("InventoryType", item.InventoryType);
                        hash.Add("Permissions", item.Permissions);
                        response.Add(hash);
                    }
                }
            }
            roothash.Add("Contents", response);
            enqueue(roothash);
        }

        public void Terrain_OnLandPatch(Simulator simulator, int x, int y, int width, float[] data)
        {
            if (x >= 16 || y >= 16)
            {
                Console.WriteLine("Bad patch coordinates, (" + x + ", " + y+")");
                return;
            }

            if (width != 16)
            {
                Console.WriteLine("Unhandled patch size " + width + "x" + width);
                return;
            }
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "LandPatch");
            hash.Add("OffsetX", x * 16);
            hash.Add("OffsetY", y * 16);
            hash.Add("Region", simulator.Name);
            hash.Add("WaterLevel", simulator.WaterHeight); // Is there anywhere better to put this?
            
            float[,] landscape = new float[16, 16];
            for (int i = 0; i < 16; ++i)
            {
                for (int j = 0; j < 16; ++j)
                {
                    landscape[i, j] = data[i * 16 + j];
                }
            }
            // Ugly hack to fix the JSON encoding.
            float[][] resp = new float[16][];
            for (int i = 0; i < 16; ++i)
            {
                resp[i] = new float[16];
                for (int j = 0; j < 16; ++j)
                {
                    resp[i][j] = landscape[i, j];
                }
            }
            hash.Add("Patch", resp);
            enqueue(hash);
        }



        public void Assets_OnAssetUploaded(AssetUpload upload)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "AssetUploaded");
            hash.Add("AssetID", upload.AssetID);
            hash.Add("TransferID", upload.XferID);
            hash.Add("ID", upload.ID);
            hash.Add("Success", upload.Success);
            enqueue(hash);
        }

        public void Groups_OnGroupProfile(GroupProfile group)
        {
            Hashtable hash = new Hashtable();
            hash.Add("MessageType", "GroupProfile");
            hash.Add("ID", group.ID);
            hash.Add("Name", group.Name);
            hash.Add("Charter", group.Charter);
            hash.Add("Founder", group.FounderID);
            hash.Add("Insignia", group.InsigniaID);
            hash.Add("MemberCount", group.GroupMembershipCount);
            hash.Add("OwnerRole", group.OwnerRole);
            hash.Add("MemberTitle", group.MemberTitle);
            hash.Add("Money", group.Money);
            hash.Add("MembershipFee", group.MembershipFee);
            hash.Add("OpenEnrollment", group.OpenEnrollment);
            hash.Add("ShowInList", group.ShowInList);
            hash.Add("AcceptNotices", group.AcceptNotices);
            hash.Add("Contribution", group.Contribution);
            enqueue(hash);
        }

        // You know, some way of telling which group this is for would be nice.
        public void Groups_OnGroupMembers(Dictionary<LLUUID, GroupMember> members)
        {
            List<Hashtable> list = new List<Hashtable>();
            foreach (KeyValuePair<LLUUID,GroupMember> memberpair in members)
            {
                GroupMember member = memberpair.Value;
                Hashtable hash = new Hashtable();
                hash.Add("LLUUID", memberpair.Key);
                hash.Add("Contribution", member.Contribution);
                hash.Add("ID", member.ID); // Maybe this is the group ID?
                hash.Add("IsOwner", member.IsOwner);
                hash.Add("OnlineStatus", member.OnlineStatus);
                hash.Add("Powers", member.Powers);
                hash.Add("Title", member.Title);
                list.Add(hash);
            }
            Hashtable message = new Hashtable();
            message.Add("MessageType", "GroupMembers");
            message.Add("MemberList", list);
            enqueue(message);
        }

        public void Groups_OnGroupNames(Dictionary<LLUUID, string> groupNames)
        {
            AjaxLife.Debug("OnGroupNames", "OnGroupNames arrived.");
            Hashtable message = new Hashtable();
            message.Add("MessageType", "GroupNames");
            message.Add("Names", groupNames);
            enqueue(message);
        }

        public void Groups_OnCurrentGroups(Dictionary<LLUUID, Group> groups)
        {
            Hashtable message = new Hashtable();
            message.Add("MessageType", "CurrentGroups");
            message.Add("Groups", groups);
            enqueue(message);
        }

        public void Self_OnGroupChatJoin(LLUUID groupChatSessionID, LLUUID tmpSessionID, bool success)
        {
            AjaxLife.Debug("Events","OnGroupChatJoin(" + groupChatSessionID + ", " + tmpSessionID + ", " + success.ToString() + ")");
            Hashtable message = new Hashtable();
            message.Add("MessageType", "GroupChatJoin");
            message.Add("GroupChatSessionID", groupChatSessionID);
            message.Add("TmpSessionID", tmpSessionID);
            message.Add("Success", success);
            enqueue(message);
        }

        public void Inventory_OnNoteUploaded(bool success, string status, LLUUID itemID, LLUUID assetID)
        {
            Hashtable message = new Hashtable();
            message.Add("MessageType", "InventoryNoteUploaded");
            message.Add("Success", success);
            message.Add("Status", status);
            message.Add("ItemID", itemID);
            message.Add("AssetID", assetID);
            enqueue(message);
        }

        public void Inventory_OnItemCreated(bool success, InventoryItem item)
        {
            Hashtable message = new Hashtable();
            message.Add("MessageType", "InventoryCreated");
            message.Add("Success", success);
            message.Add("Name", item.Name);
            message.Add("FolderID", item.ParentUUID);
            message.Add("UUID", item.UUID);
            message.Add("AssetType", item.AssetType);
            message.Add("AssetUUID", item.AssetUUID);
            message.Add("CreatorID", item.CreatorID);
            message.Add("OwnerID", item.OwnerID);
            message.Add("CreationDate", item.CreationDate);
            message.Add("Description", item.Description);
            message.Add("Flags", item.Flags);
            message.Add("InventoryType", item.InventoryType);
            message.Add("Permissions", item.Permissions);
            enqueue(message);
        }

        public void Inventory_OnItemReceived(InventoryItem item)
        {
            Hashtable message = new Hashtable();
            message.Add("MessageType", "ItemReceived");
            message.Add("Name", item.Name);
            message.Add("FolderID", item.ParentUUID);
            message.Add("UUID", item.UUID);
            message.Add("AssetType", item.AssetType);
            message.Add("AssetUUID", item.AssetUUID);
            message.Add("CreatorID", item.CreatorID);
            message.Add("OwnerID", item.OwnerID);
            message.Add("CreationDate", item.CreationDate);
            message.Add("Description", item.Description);
            message.Add("Flags", item.Flags);
            message.Add("InventoryType", item.InventoryType);
            message.Add("Permissions", item.Permissions);
            enqueue(message);
        }

        public void Inventory_OnTaskItemReceived(LLUUID itemID, LLUUID folderID, LLUUID creatorID, LLUUID assetID, InventoryType type)
        {
            Hashtable message = new Hashtable();
            message.Add("MessageType", "TaskItemReceived");
            message.Add("ItemID", itemID);
            message.Add("FolderID", folderID);
            message.Add("CreatorID", creatorID);
            message.Add("AssetID", assetID);
            message.Add("Type", (byte)type);
            enqueue(message);
        }
        
        public void Parcels_OnParcelProperties(Parcel parcel, ParcelManager.ParcelResult result, int sequenceID, bool snapSelection)
        {
            if(result == ParcelManager.ParcelResult.NoData)
            {
                Hashtable message = new Hashtable();
                message.Add("MessageType", "ParcelPropertiesFailed");
                message.Add("LocalID", parcel.LocalID);
                message.Add("SequenceID", sequenceID);
                enqueue(message);
            }
            else
            {
                Hashtable message = new Hashtable();
                message.Add("MessageType", "ParcelProperties");
                message.Add("SequenceID", sequenceID);
                message.Add("LocalID", parcel.LocalID);
                message.Add("AABBMax", parcel.AABBMax);
                message.Add("AABBMin", parcel.AABBMin);
                message.Add("AccessList", parcel.AccessList);
                message.Add("Area", parcel.Area);
                message.Add("AuctionID", parcel.AuctionID);
                message.Add("AuthBuyerID", parcel.AuthBuyerID);
                message.Add("Category", parcel.Category);
                message.Add("ClaimDate", parcel.ClaimDate);
                message.Add("ClaimPrice", parcel.ClaimPrice);
                message.Add("Desc", parcel.Desc);
                message.Add("Dwell", parcel.Dwell);
                message.Add("Flags", (uint)parcel.Flags);
                message.Add("GroupID", parcel.GroupID);
                message.Add("GroupPrims", parcel.GroupPrims);
                message.Add("IsGroupOwned", parcel.IsGroupOwned);
                message.Add("LandingType", parcel.LandingType);
                message.Add("MaxPrims", parcel.MaxPrims);
                message.Add("MediaAutoScale", parcel.MediaAutoScale);
                message.Add("MediaDesc", parcel.MediaDesc);
                message.Add("MediaHeight", parcel.MediaHeight);
                message.Add("MediaID", parcel.MediaID);
                message.Add("MediaLoop", parcel.MediaLoop);
                message.Add("MediaType", parcel.MediaType);
                message.Add("MediaURL", parcel.MediaURL);
                message.Add("MediaWidth", parcel.MediaWidth);
                message.Add("MusicURL", parcel.MusicURL);
                message.Add("Name", parcel.Name);
                message.Add("ObscureMedia", parcel.ObscureMedia);
                message.Add("ObscureMusic", parcel.ObscureMusic);
                message.Add("OtherCleanTime", parcel.OtherCleanTime);
                message.Add("OtherPrims", parcel.OtherPrims);
                message.Add("OwnerPrims", parcel.OwnerPrims);
                message.Add("OwnerID", parcel.OwnerID);
                message.Add("PrimBonus", parcel.ParcelPrimBonus);
                message.Add("PassHours", parcel.PassHours);
                message.Add("PassPrice", parcel.PassPrice);
                message.Add("PublicCount", parcel.PublicCount);
                message.Add("RegionDenyAgeUnverified", parcel.RegionDenyAgeUnverified);
                message.Add("RegionDenyAnonymous", parcel.RegionDenyAnonymous);
                message.Add("RegionPushOverride", parcel.RegionPushOverride);
                message.Add("RentPrice", parcel.RentPrice);
                message.Add("SalePrice", parcel.SalePrice);
                message.Add("SelectedPrims", parcel.SelectedPrims);
                message.Add("SelfCount", parcel.SelfCount);
                message.Add("SimWideMaxPrims", parcel.SimWideMaxPrims);
                message.Add("SimWideTotalPrims", parcel.SimWideTotalPrims);
                message.Add("SnapshotID", parcel.SnapshotID);
                message.Add("Status", parcel.Status);
                message.Add("TotalPrims", parcel.TotalPrims);
                message.Add("UserLocation", parcel.UserLocation);
                message.Add("UserLookAt", parcel.UserLookAt);
                enqueue(message);
            }
        }
    }
}
