﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.BitcoinCore;
using NBitcoin.Protocol;
using Stratis.Bitcoin.BlockStore;
using Stratis.Bitcoin.Connection;
using Stratis.Bitcoin.MemoryPool;
using Xunit;

namespace Stratis.Bitcoin.Tests
{
    public class BlockStoreTests
    {
		[Fact]
		public void BlockRepositoryPutGetDeleteBlock()
	    {
			using (var dir = TestDirectory.Create())
			{
				using (var blockRepo = new BlockStore.BlockRepository(dir.FolderName))
				{
					var lst = new List<Block>();
					for (int i = 0; i < 5; i++)
					{
						// put
						var block = new Block();
						block.AddTransaction(new Transaction());
						block.AddTransaction(new Transaction());
						block.UpdateMerkleRoot();
						block.Header.HashPrevBlock = lst.Any() ? lst.Last().GetHash() : Network.Main.GenesisHash;
						blockRepo.PutAsync(block).GetAwaiter().GetResult();

						// get
						var received = blockRepo.GetAsync(block.GetHash()).GetAwaiter().GetResult();
						Assert.True(block.ToBytes().SequenceEqual(received.ToBytes()));

						lst.Add(block);
					}

					// check each block
					foreach (var block in lst)
					{
						var received = blockRepo.GetAsync(block.GetHash()).GetAwaiter().GetResult();
						Assert.True(block.ToBytes().SequenceEqual(received.ToBytes()));
					}

					// delete
					blockRepo.DeleteAsync(lst.ElementAt(2).GetHash());
					var deleted = blockRepo.GetAsync(lst.ElementAt(2).GetHash()).GetAwaiter().GetResult();
					Assert.Null(deleted);
				}
			}
		}

		[Fact]
		public void BlockBroadcastInv()
	    {
			using (NodeBuilder builder = NodeBuilder.Create())
			{
				var stratisNodeSync = builder.CreateStratisNode();
				var stratisNode1 = builder.CreateStratisNode();
				var stratisNode2 = builder.CreateStratisNode();
				builder.StartAll();

				// generate blocks and wait for the downloader to pickup
				stratisNodeSync.SetDummyMinerSecret(new BitcoinSecret(new Key(), stratisNodeSync.FullNode.Network));
				stratisNodeSync.GenerateStratis(10); // coinbase maturity = 10
				// wait for block repo for block sync to work
				Class1.Eventually(() => stratisNodeSync.FullNode.Chain.Tip.HashBlock == stratisNodeSync.FullNode.ConsensusLoop.Tip.HashBlock);
				Class1.Eventually(() => stratisNodeSync.FullNode.BlockStoreManager.BlockRepository.GetAsync(stratisNodeSync.CreateRPCClient().GetBestBlockHash()).Result != null);

				// sync both nodes
				stratisNode1.CreateRPCClient().AddNode(stratisNodeSync.Endpoint, true);
				stratisNode2.CreateRPCClient().AddNode(stratisNodeSync.Endpoint, true);
				Class1.Eventually(() => stratisNode1.CreateRPCClient().GetBestBlockHash() == stratisNodeSync.CreateRPCClient().GetBestBlockHash());
				Class1.Eventually(() => stratisNode2.CreateRPCClient().GetBestBlockHash() == stratisNodeSync.CreateRPCClient().GetBestBlockHash());

				// set node2 to use inv (not headers)
				stratisNode2.FullNode.ConnectionManager.ConnectedNodes.First().Behavior<BlockStoreBehavior>().PreferHeaders = false;

				// generate two new blocks
				stratisNodeSync.GenerateStratis(2);
				// wait for block repo for block sync to work
				Class1.Eventually(() => stratisNodeSync.FullNode.Chain.Tip.HashBlock == stratisNodeSync.FullNode.ConsensusLoop.Tip.HashBlock);
				Class1.Eventually(() => stratisNodeSync.FullNode.BlockStoreManager.BlockRepository.GetAsync(stratisNodeSync.CreateRPCClient().GetBestBlockHash()).Result != null);

				// wait for the other nodes to pick up the newly generated blocks
				Class1.Eventually(() => stratisNode1.CreateRPCClient().GetBestBlockHash() == stratisNodeSync.CreateRPCClient().GetBestBlockHash());
				Class1.Eventually(() => stratisNode2.CreateRPCClient().GetBestBlockHash() == stratisNodeSync.CreateRPCClient().GetBestBlockHash());
			}
		}
    }
}