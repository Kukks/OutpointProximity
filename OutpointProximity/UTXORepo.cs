using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Logging;

namespace OutpointProximity;

public class UTXORepo
{
    private readonly ScriptPubKeyType _scriptPubKeyType;

    public UTXORepo(Network network, ScriptPubKeyType scriptPubKeyType)
    {
        Network = network;
        _scriptPubKeyType = scriptPubKeyType;
    }

    public Network Network { get; }
    public ConcurrentDictionary<Script, HashSet<Script>> ScriptLinks { get; set; } = new();
    public ConcurrentDictionary<Script, HashSet<OutPoint>> Utxos { get; set; } = new();
    public ConcurrentDictionary<Script, Key> Keys { get; set; } = new();
    public ConcurrentDictionary<string, HashSet<Script>> LabelsToScripts { get; set; } = new();

    public Script GenerateScript(string[] labels)
    {
        var key = new Key();
        var script = key.PubKey.GetScriptPubKey(_scriptPubKeyType);
        Utxos.TryAdd(script, new HashSet<OutPoint>());
        Keys.TryAdd(script, key);
        AddLabelsToScript(script, labels);
        Console.WriteLine($"Registered address {script.GetDestinationAddress(Network)}");
        return script;
    }

    public void AddLabelsToScript(Script script, string[] labels)
    {
        if (!labels.Any())
        {
            return;
        }

        Console.WriteLine(
            $"Adding labels {string.Join(", ", labels)} to address {script.GetDestinationAddress(Network).ToString()}");
        foreach (var label in labels)
        {
            LabelsToScripts.TryAdd(label, new HashSet<Script>());
            if (LabelsToScripts.TryGetValue(label, out var scriptsWithLabel))
            {
                scriptsWithLabel.Add(script);
            }
        }
    }

    public void ProcessTransaction(Transaction transaction, string[] labels)
    {
        foreach (var transactionOutput in transaction.Outputs.AsIndexedOutputs())
        {
            var outpoint = new OutPoint(transaction.GetHash(), transactionOutput.N);
            var script = transactionOutput.TxOut.ScriptPubKey;
            if (Utxos.TryGetValue(script, out var utxos))
            {
                Console.WriteLine(
                    $"received utxo {outpoint} to address {script.GetDestinationAddress(Network).ToString()}");
                utxos.Add(outpoint);

                foreach (var transactionInput in transaction.Inputs.AsIndexedInputs())
                {
                    var inputScript = transactionInput.TxIn.GetSigner()?.ScriptPubKey;
                    if (inputScript is null)
                    {
                        continue;
                    }

                    ScriptLinks.TryAdd(inputScript, new HashSet<Script>());
                    if (ScriptLinks.TryGetValue(inputScript, out var scriptsLinked))
                    {
                        scriptsLinked.Add(script);
                    }
                }
            }

            AddLabelsToScript(script, labels);
        }

        foreach (var transactionInput in transaction.Inputs.AsIndexedInputs())
        {
            var outpoint = transactionInput.PrevOut;
            var script = transactionInput.TxIn.GetSigner()?.ScriptPubKey;

            if (Utxos.TryGetValue(script, out var utxos))
            {
                Console.WriteLine(
                    $"spent utxo {outpoint} to address {script.GetDestinationAddress(Network).ToString()}");
                utxos.Remove(outpoint);
                if (!utxos.Any())
                {
                    // Utxos.Remove(script, out utxos);
                }

                ScriptLinks.TryAdd(script, new HashSet<Script>());
                if (ScriptLinks.TryGetValue(script, out var scriptsLinked))
                {
                    foreach (var transactionOutput in transaction.Outputs.AsIndexedOutputs())
                    {
                        scriptsLinked.Add(transactionOutput.TxOut.ScriptPubKey);
                    }
                }
            }
        }
    }

    public HashSet<string> GetLabelsOfScript(Script script)
    {
        return LabelsToScripts.Where(pair => pair.Value.Contains(script)).Select(pair => pair.Key)
            .ToHashSet();
    }

    public HashSet<Script> GetScriptLinks(Script script)
    {
        ScriptLinks.TryGetValue(script, out var childLinks);
        childLinks ??= new HashSet<Script>();
        return ScriptLinks.Where(pair => pair.Value.Contains(script))
            .SelectMany(pair => pair.Value.Concat(new[] {pair.Key})).Concat(childLinks).Where(script1 => script1 != script).ToHashSet();
    }

    public HashSet<Script> GetScriptsByProximity(ProximityParameters parameters)
    {
        if (!parameters.Include)
        {
            var result = GetScriptsByProximity(parameters with {Include = true});
            return Utxos.Where(pair => !result.Contains(pair.Key)).Select(pair => pair.Key).ToHashSet();
        }

        if (LabelsToScripts.TryGetValue(parameters.Label, out var associatedScripts))
        {
            HashSet<Script> result = new HashSet<Script>(associatedScripts);
            for (int i = 0; i < parameters.Distance; i++)
            {
                var newAssociatedScripts = new HashSet<Script>();
                foreach (var associatedScript in associatedScripts)
                {
                    if (ScriptLinks.TryGetValue(associatedScript, out var depthAssociatedScripts))
                    {
                        newAssociatedScripts = newAssociatedScripts.Concat(depthAssociatedScripts).ToHashSet();
                        result = result.Concat(depthAssociatedScripts).ToHashSet();
                    }

                    foreach (var keyValuePair in ScriptLinks)
                    {
                        if (keyValuePair.Value.Contains(associatedScript))
                        {
                            newAssociatedScripts = newAssociatedScripts.Concat(keyValuePair.Value).ToHashSet();
                            newAssociatedScripts.Add(keyValuePair.Key);
                            result = result.Concat(keyValuePair.Value).ToHashSet();
                            result.Add(keyValuePair.Key);
                        }
                    }
                }

                associatedScripts = newAssociatedScripts;
            }

            return Utxos.Where(pair => result.Contains(pair.Key)).Select(pair => pair.Key).ToHashSet();
            ;
        }

        return new HashSet<Script>();
    }

    string GetValue(string search, Dictionary<string, string> dictionary)
    {
        foreach (var pair in dictionary)
        {
            if (pair.Key == search)
            {
                return pair.Value;
            }
            else if (pair.Value == search)
            {
                return pair.Key;
            }
        }

        return null;
    }

    public record ProximityParameters
    {
        public string Label { get; set; }
        public int Distance { get; set; }
        public bool Include { get; set; }
    }
}