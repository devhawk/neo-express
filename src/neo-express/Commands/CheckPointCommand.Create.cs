﻿using McMaster.Extensions.CommandLineUtils;
using System;
using System.IO;
using System.IO.Compression;

namespace Neo.Express.Commands
{
    internal partial class CheckPointCommand
    {
        [Command("create")]
        private class Create
        {
            [Argument(0)]
            private string Name { get; }

            [Option]
            private string Input { get; }

            [Option]
            private bool Force { get; }

            private int OnExecute(CommandLineApplication app, IConsole console)
            {
                try
                {
                    var filename = string.IsNullOrEmpty(Name)
                        ? $"{DateTimeOffset.Now:yyyyMMdd-hhmmss}{CHECKPOINT_EXTENSION}"
                        : Name + CHECKPOINT_EXTENSION;

                    if (File.Exists(filename))
                    {
                        if (!Force)
                        {
                            throw new Exception("You must specify --force to overwrite an existing file");
                        }

                        File.Delete(filename);
                    }

                    var (devChain, _) = DevChain.Load(Input);

                    if (devChain.ConsensusNodes.Count > 1)
                    {
                        throw new Exception("Checkpoint create is only supported on single node express instances");
                    }

                    var consensusNode = devChain.ConsensusNodes[0];
                    var blockchainAccount = consensusNode.Wallet.DefaultAccount;
                    var blockchainPath = consensusNode.GetBlockchainPath();

                    string checkpointTempPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                    if (Directory.Exists(checkpointTempPath))
                    {
                        Directory.Delete(checkpointTempPath, true);
                    }

                    using (var db = new Persistence.RocksDbStore(blockchainAccount.GetBlockchainPath()))
                    {
                        db.CheckPoint(checkpointTempPath);
                        File.WriteAllText(Path.Combine(checkpointTempPath, ADDRESS_FILENAME), blockchainAccount.Address);
                        ZipFile.CreateFromDirectory(checkpointTempPath, filename);
                        console.WriteLine($"created checkpoint {Path.GetFileName(filename)}");
                    }

                    Directory.Delete(checkpointTempPath, true);

                    return 0;
                }
                catch (Exception ex)
                {
                    console.WriteError(ex.Message);
                    app.ShowHelp();
                    return 1;
                }
            }
        }
    }
}