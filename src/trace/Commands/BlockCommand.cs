using System.ComponentModel.DataAnnotations;
using McMaster.Extensions.CommandLineUtils;
using Neo;
using OneOf;
using static Neo.BlockchainToolkit.Utility;

namespace NeoTrace.Commands
{
    [Command("block", Description = "Trace all transactions in a specified block")]
    class BlockCommand
    {
        [Argument(0, Description = "Block index or hash")]
        [Required]
        internal string BlockIdentifier { get; } = string.Empty;

        [Option(Description = "URL of Neo JSON-RPC Node\nSpecify MainNet (default), TestNet or JSON-RPC URL")]
        internal string RpcUri { get; } = string.Empty;

        internal async Task<int> OnExecuteAsync(CommandLineApplication app, IConsole console)
        {
            try
            {
                if (!TryParseRpcUri(RpcUri, out var uri))
                {
                    throw new ArgumentException($"Invalid RpcUri value \"{RpcUri}\"");
                }
                var blockId = ParseBlockIdentifier();

                await Program.TraceBlockAsync(uri, blockId, console).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                await app.Error.WriteLineAsync(ex.Message);
                return 1;
            }
        }

        OneOf<uint, UInt256> ParseBlockIdentifier()
        {
            if (uint.TryParse(BlockIdentifier, out var index))
            {
                if (index == 0) throw new ArgumentException("Cannot trace genesis block");
                return index;
            }
            if (UInt256.TryParse(BlockIdentifier, out var hash)) return hash;

            throw new ArgumentException($"Invalid Block Identifier {BlockIdentifier}");
        }
    }
}