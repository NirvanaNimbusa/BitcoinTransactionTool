﻿// Bitcoin Transaction Tool
// Copyright (c) 2017 Coding Enthusiast
// Distributed under the MIT software license, see the accompanying
// file LICENCE or http://www.opensource.org/licenses/mit-license.php.

using BitcoinTransactionTool.Backend;
using BitcoinTransactionTool.Backend.MVVM;
using BitcoinTransactionTool.Models;
using BitcoinTransactionTool.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace BitcoinTransactionTool.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        public MainWindowViewModel()
        {
            // Initializing lists:
            ApiList = Enum.GetValues(typeof(TxApiNames)).Cast<TxApiNames>();
            WalletTypeList = Enum.GetValues(typeof(WalletType)).Cast<WalletType>();
            SendAddressList = new BindingList<SendingAddress>();
            UtxoList = new BindingList<UTXO>();
            ReceiveList = new BindingList<ReceivingAddress>();

            WinMan = new WindowManager();

            // Initializing Commands.
            GetUTXOCommand = new RelayCommand(GetUTXO, () => !IsReceiving);
            MakeTxCommand = new RelayCommand(MakeTx, CanMakeTx);
            CopyTxCommand = new RelayCommand(CopyTx, () => !string.IsNullOrEmpty(RawTx));
            ShowQrWindowCommand = new RelayCommand(ShowQrWindow, () => !string.IsNullOrEmpty(RawTx));
            ShowJsonWindowCommand = new RelayCommand(ShowJsonWindow, () => !string.IsNullOrEmpty(RawTx));
            ShowEditWindowCommand = new RelayCommand(ShowEditWindow);

            // These moved below to avoid throwing null exception.
            ReceiveList.ListChanged += ReceiveList_ListChanged;
            SelectedUTXOs = new ObservableCollection<UTXO>();
            SelectedWalletType = WalletType.Normal;
        }


        #region Properties

        public string TitleVersion
        {
            get
            {
                Version ver = Assembly.GetExecutingAssembly().GetName().Version;
                return $"Bitcoin Transaction Tool - Version {((ver.Major == 0) ? "Beta" : ver.ToString(2))}";
            }
        }

        public string VerString => Assembly.GetExecutingAssembly().GetName().Version.ToString(4);


        /// <summary>
        /// Indicating an active connection.
        /// <para/> Used to enable/disable buttons
        /// </summary>
        public bool IsReceiving
        {
            get => _isReceiving;
            set
            {
                if (SetField(ref _isReceiving, value))
                {
                    GetUTXOCommand.RaiseCanExecuteChanged();
                }
            }
        }
        private bool _isReceiving;

        /// <summary>
        /// List of Api services used for receiving Unspent Transaction Outputs (UTXO)
        /// </summary>
        public IEnumerable<TxApiNames> ApiList { get; set; }


        /// <summary>
        /// Api service that will be used to retreive UTXOs.
        /// </summary>
        public TxApiNames SelectedApi
        {
            get => _selectedApi;
            set { SetField(ref _selectedApi, value); }
        }
        private TxApiNames _selectedApi;


        /// <summary>
        /// List of Bitcoin Addresses to receive their UTXOs and use for spending.
        /// </summary>
        public BindingList<SendingAddress> SendAddressList { get; set; }


        /// <summary>
        /// List of all UTXOs that can be used for spending.
        /// </summary>
        public BindingList<UTXO> UtxoList
        {
            get => _uTXOList;
            set { SetField(ref _uTXOList, value); }
        }
        private BindingList<UTXO> _uTXOList;


        private uint _txVer = 1;
        public uint TxVersion
        {
            get => _txVer;
            set { SetField(ref _txVer, value); }
        }


        /// <summary>
        /// LockTime value used in transactions.
        /// </summary>
        public uint LockTime
        {
            get => _lt;
            set { SetField(ref _lt, value); }
        }
        private uint _lt;


        /// <summary>
        /// Estimated size of the transaction based on number of inputs and outputs.
        /// </summary>
        [DependsOnProperty(nameof(SelectedUTXOs))]
        public int TransactionSize
        {
            get
            {
                //int size = Transaction.GetTransactionSize(SelectedUTXOs.Count, ReceiveList.Count);
                int size = txBuilder.GetEstimatedTransactionSize(SelectedUTXOs.ToArray(), ReceiveList.ToArray());
                return size;
            }
        }


        /// <summary>
        /// Sum of Sending Addresses balances which shows the total available amount to spend.
        /// </summary>
        public decimal TotalBalance => SendAddressList.Sum(x => x.Balance);


        /// <summary>
        /// Sum of selected UTXOs amounts, which is amount that is about to be spent.
        /// </summary>
        [DependsOnProperty(nameof(SelectedUTXOs))]
        public decimal TotalSelectedBalance => SelectedUTXOs.Sum(x => x.AmountBitcoin);


        /// <summary>
        /// Total amount which is being sent to all the Receiving Addresses.
        /// </summary>
        [DependsOnProperty(nameof(ReceiveList))]
        public decimal TotalToSend => ReceiveList.Sum(x => x.Payment);


        /// <summary>
        /// Amount of fee which is being paid (Must be >= 0).
        /// </summary>
        [DependsOnProperty(nameof(TotalSelectedBalance), nameof(TotalToSend), nameof(SelectedUTXOs))]
        public decimal Fee => TotalSelectedBalance - TotalToSend;


        /// <summary>
        /// Amount of fee in satoshi per byte based on estimated transaction size and fee amount.
        /// </summary>
        [DependsOnProperty(nameof(TransactionSize), nameof(Fee), nameof(SelectedUTXOs))]
        public string FeePerByte => $"{((TransactionSize == 0) ? 0 : ((int)(Fee / Constants.Satoshi) / TransactionSize))} satoshi/byte";


        /// <summary>
        /// List of selected UTXOs, these are the ones that will be spent.
        /// </summary>
        public ObservableCollection<UTXO> SelectedUTXOs
        {
            get => _selectedUTXOs;
            set
            {
                if (SetField(ref _selectedUTXOs, value))
                {
                    MakeTxCommand.RaiseCanExecuteChanged();
                }
            }
        }
        private ObservableCollection<UTXO> _selectedUTXOs;


        /// <summary>
        /// List of addresses to send coins to.
        /// </summary>
        public BindingList<ReceivingAddress> ReceiveList { get; set; }

        void ReceiveList_ListChanged(object sender, ListChangedEventArgs e)
        {
            RaisePropertyChanged(nameof(Fee));
            RaisePropertyChanged(nameof(FeePerByte));
            RaisePropertyChanged(nameof(TotalToSend));
            RaisePropertyChanged(nameof(TransactionSize));

            MakeTxCommand.RaiseCanExecuteChanged();
        }


        /// <summary>
        /// Used for setting wallet type which would indicate which special <see cref="SignatureScript"/>
        /// is needed for cold storage to sign the raw transaction
        /// </summary>
        public IEnumerable<WalletType> WalletTypeList { get; set; }

        private WalletType _selectedWalletType;
        public WalletType SelectedWalletType
        {
            get => _selectedWalletType;
            set => SetField(ref _selectedWalletType, value);
        }


        /// <summary>
        /// Raw Unsigned Transaction result.
        /// </summary>
        private string _rawTx;
        public string RawTx
        {
            get => _rawTx;
            set
            {
                if (SetField(ref _rawTx, value))
                {
                    CopyTxCommand.RaiseCanExecuteChanged();
                    ShowQrWindowCommand.RaiseCanExecuteChanged();
                    ShowJsonWindowCommand.RaiseCanExecuteChanged();
                }
            }
        }


        
        public IWindowManager WinMan { get; set; }
        private readonly TxBuilder txBuilder = new TxBuilder();

        #endregion


        #region commands

        /// <summary>
        /// Contacts the selected Api service and receives the UTXO list.
        /// </summary>
        public RelayCommand GetUTXOCommand { get; private set; }
        private async void GetUTXO()
        {
            Status = "Receiving Unspent Transaction Outputs...";
            Errors = string.Empty;
            IsReceiving = true;

            TransactionApi api;
            switch (SelectedApi)
            {
                case TxApiNames.BlockCypher:
                    api = new Services.TransactionServices.BlockCypher();
                    break;
                case TxApiNames.BlockchainInfo:
                default:
                    api = new Services.TransactionServices.BlockchainInfo();
                    break;
            }
            Response<List<UTXO>> resp = await api.GetUTXO(SendAddressList.ToList());
            if (!resp.Errors.Any())
            {
                UtxoList = new BindingList<UTXO>(resp.Result);
                foreach (var addr in SendAddressList)
                {
                    addr.BalanceSatoshi = 0;
                    UtxoList.ToList().ForEach(x => addr.BalanceSatoshi += (x.Address == addr.Address) ? x.Amount : 0);
                    RaisePropertyChanged(nameof(TotalBalance));
                }
                Status = "Finished successfully.";
            }
            else
            {
                Status = "Encountered an error!";
                Errors = resp.Errors.GetErrors();
            }

            IsReceiving = false;
        }


        /// <summary>
        /// Creates the Raw Unsigned Transaction.
        /// </summary>
        public RelayCommand MakeTxCommand { get; private set; }
        private void MakeTx()
        {
            var tx = txBuilder.Build(TxVersion, SelectedUTXOs.Cast<UTXO>().ToList(), ReceiveList.ToList(), LockTime);
            RawTx = tx.Serialize().ToBase16();
        }
        private bool CanMakeTx()
        {
            if (SendAddressList.Count == 0 || SelectedUTXOs.Count == 0 || ReceiveList.Count == 0)
            {
                return false;
            }
            if (SendAddressList.Select(x => x.HasErrors).Contains(true) || ReceiveList.Select(x => x.HasErrors).Contains(true))
            {
                return false;
            }
            if (Fee < 0)
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// Builds QR code representing the Raw Transaction in a new window.
        /// </summary>
        public RelayCommand ShowQrWindowCommand { get; private set; }
        private void ShowQrWindow()
        {
            QrViewModel vm = new QrViewModel()
            {
                RawTx = this.RawTx,
                SelectedInEncoder = QrViewModel.Encoders.Base16,
                SelectedOutEncoder = QrViewModel.Encoders.Base16
            };

            WinMan.Show(vm, "QR");
        }


        /// <summary>
        /// Copies the created RawTx to clipboard.
        /// </summary>
        public RelayCommand CopyTxCommand { get; private set; }
        private void CopyTx()
        {
            Clipboard.SetText(RawTx);
        }


        /// <summary>
        /// Opens a new window to represent the RawTx as JSON string.
        /// </summary>
        public RelayCommand ShowJsonWindowCommand { get; private set; }
        private void ShowJsonWindow()
        {
            TxJsonViewModel vm = new TxJsonViewModel
            {
                RawTx = RawTx
            };

            WinMan.Show(vm, "Transaction JSON");
        }


        /// <summary>
        /// Opens a new Window for handling deserializaion of transactions and Pushing them to the bitcoin network if they are signed.
        /// </summary>
        public RelayCommand ShowEditWindowCommand { get; private set; }
        private void ShowEditWindow()
        {
            TransactionEditViewModel vm = new TransactionEditViewModel
            {
                RawTx = RawTx
            };

            WinMan.Show(vm, "Transaction Edit");
        }


        public RelayCommand ShowScriptWindowCommand => new RelayCommand(ShowScriptWindow);
        private void ShowScriptWindow()
        {
            ScriptViewModel vm = new ScriptViewModel();

            WinMan.Show(vm, "Script writer");
        }

        #endregion
    }
}
