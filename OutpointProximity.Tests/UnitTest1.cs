using NBitcoin;

namespace OutpointProximity.Tests;

public class Tests
{
    [SetUp]
    public void Setup()
    {
        Repo = new UTXORepo(Network.Main, ScriptPubKeyType.Segwit);
    }

    private UTXORepo Repo { get; set; }

    [Test]
    public void Test1()
    {
        var exchangeDepositAddress = new Key().PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit);
        Repo.AddLabelsToScript(exchangeDepositAddress, new []{"exchange"});
        
        var taxPaymentAddress = new Key().PubKey.GetScriptPubKey(ScriptPubKeyType.Segwit);
        Repo.AddLabelsToScript(taxPaymentAddress, new []{"tax-department"});
        
        var exchangeWithdrawAddress = Repo.GenerateScript(new []{"exchange"});
        var taxRefundAddress = Repo.GenerateScript(new []{"tax-refund"});


        //We receive money from the tax department
        var receiveMoneyFromTax = CreateTransaction(
            new[]
            {
                new CoinWithKey(Repo.Network, new Key(), CreateOutPoint())
            },
            new TxOut[]
            {
                new(Money.Coins(1m), taxRefundAddress)
            });
        
        Repo.ProcessTransaction(receiveMoneyFromTax, new string[]{});
        
        
        //We also withdraw money from an exchange
        var receiveMoneyFromExchange = CreateTransaction(
            new[]
            {
                new CoinWithKey(Repo.Network, new Key(), CreateOutPoint())
            },
            new TxOut[]
            {
                new(Money.Coins(0.2m), exchangeWithdrawAddress)
            });
        
        Repo.ProcessTransaction(receiveMoneyFromExchange, new string[]{});
        
        //get utxos labelled as "exchange"
        var zeroDistanceExchangeInclude =  Repo.GetScriptsByProximity(new UTXORepo.ProximityParameters()
        {
            Label = "exchange",
            Include = true,
            Distance = 0
        });
        
        Assert.That(zeroDistanceExchangeInclude.Count, Is.EqualTo(1));
        Assert.True(zeroDistanceExchangeInclude.Contains(exchangeWithdrawAddress) );
        
        //get utxos NOT labelled as "exchange"
        var zeroDistanceExchangeExclude = Repo.GetScriptsByProximity(new UTXORepo.ProximityParameters()
        {
            Label = "exchange",
            Include = false,
            Distance = 0
        });
        
        Assert.That(zeroDistanceExchangeExclude.Count, Is.EqualTo(1));
        Assert.True(zeroDistanceExchangeExclude.Contains(taxRefundAddress) );
        
        
        //get utxos labelled as "exchange" with 1 distance
        var oneDistanceExchangeInclude =  Repo.GetScriptsByProximity(new UTXORepo.ProximityParameters()
        {
            Label = "exchange",
            Include = true,
            Distance = 1
        });
        // should be the same as there are no txs linking it to anything else
        Assert.That(oneDistanceExchangeInclude.Count, Is.EqualTo(1));
        Assert.True(oneDistanceExchangeInclude.Contains(exchangeWithdrawAddress) );

        var ownWalletScript = Repo.GenerateScript(Array.Empty<string>());
        
        
        //create a tx that takes the funds taht  came from exchange and from tax and comvbines them to 1 output to own wallet
        var consolidateUtxos = CreateTransaction(
            Repo.Utxos.SelectMany(pair => pair.Value.Select(point => new CoinWithKey(Repo.Network, Repo.Keys[pair.Key], point))).ToArray(),
            new TxOut[]
            {
                new(Money.Coins(1.2m), ownWalletScript)
            });
        
        Repo.ProcessTransaction(consolidateUtxos, new string[]{});
        
        
        oneDistanceExchangeInclude =  Repo.GetScriptsByProximity(new UTXORepo.ProximityParameters()
        {
            Label = "exchange",
            Include = true,
            Distance = 1
        });
        // should be the same as there are no txs linking it to anything else
        // Assert.That(oneDistanceExchangeInclude.Count, Is.EqualTo(1));
        Assert.True(oneDistanceExchangeInclude.Contains(ownWalletScript) );

    }
    
    
    public static OutPoint CreateOutPoint()
        => new(RandomUtils.GetUInt256(), (uint)RandomUtils.GetInt32());


    public Transaction CreateTransaction(CoinWithKey[] inputs, TxOut[] outputs)
    {
        var tx = Repo.Network.CreateTransaction();
        foreach (var input in inputs)
        {
            var txin = Repo.Network.Consensus.ConsensusFactory.CreateTxIn();
            txin.PrevOut = input.Outpoint;
            
            tx.Inputs.Add(txin);

        }

        foreach (var output in outputs)
        {
            var txout = Repo.Network.Consensus.ConsensusFactory.CreateTxOut();
            txout.Value = output.Value;
            txout.ScriptPubKey = output.ScriptPubKey;
            tx.Outputs.Add(txout);
        }

        tx.Sign(inputs.Select(key => key.Secret), inputs);

        return tx;
    }

    public class CoinWithKey : Coin
    {
        private readonly Network _network;
        private readonly Key _key;

        public CoinWithKey(Network network, Key key, OutPoint outPoint)
        {
            _network = network;
            _key = key;
            Outpoint = outPoint;
            TxOut = new TxOut(Amount, Script);
        }

        public BitcoinSecret Secret => _key.GetWif(_network);
        public Script Script => _key.GetScriptPubKey(ScriptPubKeyType.Segwit);
    }
    
}