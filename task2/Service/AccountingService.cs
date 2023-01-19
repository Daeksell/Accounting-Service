using Microsoft.EntityFrameworkCore;
using System;

namespace Service
{
    public class AccountingService
    {

        public AccountingService(BankingContext dbContext)
        {
            DbContext = dbContext;
        }
        private readonly object _synchronizationObject = new();
        private BankingContext DbContext { get; }


        public Account CreateNewAccount(string name, string currency, decimal initialBalance)
        {
            if (string.IsNullOrEmpty(currency))
            {
                throw new InvalidOperationException("Cannot create account without specifying currency");
            }
            var account = new Account() { Name = name, Currency = currency, Balance = 0 };
            lock (_synchronizationObject)
            {
                DbContext.Accounts.Add(account);
                DbContext.SaveChanges();
            }
            CreateIncomingTransaction(account.Id, initialBalance, account.Currency);
            return account;
        }

        public Transaction UpdateTransactionStatus(int transactionId, TransactionStatus status)
        {
            var transaction = DbContext.Transactions.Find(transactionId);
            _ = transaction ?? throw new InvalidOperationException($"Transaction id={transactionId} does not exist.");

            if (!TryRecalculateBalance(transaction, status))
            {
                return transaction;
            }
            transaction.Status = status;
            lock (_synchronizationObject)
            {
                DbContext.Transactions.Update(transaction);
                SaveChangesConcurrencyCheckEnabled();
            }

            return transaction;


        }

        public Transaction CreateIncomingTransaction(int accountId, decimal amount, string currency = null,
                                                                                    TransactionStatus status = TransactionStatus.Completed,
                                                                                    TransactionType type = TransactionType.Transfer)
        {
            return CreateTransaction(new Transaction
            {
                AccountId = accountId,
                Currency = currency,
                Amount = amount,
                Status = status,
                Type = type,
                Direction = TransactionDirection.Income
            });
        }

        public Transaction CreateOutcomingTransaction(int accountId, decimal amount, string currency = null,
                                                                                    TransactionStatus status = TransactionStatus.Completed,
                                                                                    TransactionType type = TransactionType.Transfer)
        {
            return CreateTransaction(new Transaction
            {
                AccountId = accountId,
                Currency = currency,
                Amount = amount,
                Status = status,
                Type = type,
                Direction = TransactionDirection.Outcome
            });
        }

        public (Transaction outcoming, Transaction incoming) CreateTransferTransactions(int senderId, int receiverId, decimal amount)
        {
            var sender = DbContext.Accounts.Find(senderId);
            var receiver = DbContext.Accounts.Find(receiverId);
            if (sender.Currency != receiver.Currency)
            {
                throw new InvalidOperationException($"Accounts id={senderId}({sender.Currency}) id={receiverId}({receiver.Currency}) are using different currencies");
            }
            var currency = sender.Currency;
            var outcomingTransaction = CreateTransaction(new Transaction
            {
                AccountId = sender.Id,
                Currency = currency,
                Amount = amount,
                Status = TransactionStatus.Completed,
                Type = TransactionType.Transfer,
                Direction = TransactionDirection.Outcome
            }, shouldSave: false);

            var incomingTransaction = CreateTransaction(new Transaction()
            {
                AccountId = receiverId,
                Currency = receiver.Currency,
                Amount = amount,
                Status = outcomingTransaction.Status,
                Type = TransactionType.Transfer,
                Direction = TransactionDirection.Income,
            }, shouldSave: false);

            lock (_synchronizationObject)
            {
                SaveChangesConcurrencyCheckEnabled();
            }

            return (outcomingTransaction, incomingTransaction);
        }

        private Transaction CreateTransaction(Transaction transaction, bool shouldSave = true)
        {
            var accountId = transaction.AccountId;
            if (accountId == default)
            {
                throw new InvalidOperationException("Transaction must be given an accountId");
            }
            if (transaction.Amount < 0)
            {
                throw new InvalidOperationException("Transaction amount cannot be less than 0");
            }
            var account = DbContext.Accounts.Find(accountId);
            _ = account ?? throw new InvalidOperationException($"Account with id={accountId} does not exist");
            var currency = transaction.Currency ??= account.Currency;
            if (account.Currency != currency)
            {
                throw new InvalidOperationException($"Transactions for Account id={accountId} should be conducted in {account.Currency}");
            }
            if (!TryCalculateNewBalance(account, transaction))
            {
                transaction.Status = TransactionStatus.Declined;
            }
            lock (_synchronizationObject)
            {
                DbContext.Transactions.Add(transaction);
                if (shouldSave)
                {
                    SaveChangesConcurrencyCheckEnabled();
                }
            }
            return transaction;

        }

        private bool TryCalculateNewBalance(Account account, Transaction transaction)
        {

            if (transaction.Status == TransactionStatus.Pending
                || transaction.Status == TransactionStatus.Declined)
            {
                return true;
            }
            lock (_synchronizationObject)
            {
                var currentBalance = account.Balance;
                var direction = transaction.Direction;
                var newBalance = 0.0m;
                if (direction == TransactionDirection.Outcome)
                {
                    newBalance = currentBalance - transaction.Amount;
                }
                else if (direction == TransactionDirection.Income)
                {
                    newBalance = currentBalance + transaction.Amount;
                }

                if (newBalance < 0)
                {
                    return false;
                }

                if (newBalance != currentBalance)
                {

                    account.Balance = newBalance;
                    DbContext.Accounts.Update(account);
                }
                return true;
            }


        }

        private bool TryRecalculateBalance(Transaction transaction, TransactionStatus status)
        {
            var oldStatus = transaction.Status;
            if (oldStatus == status)
            {
                return true;
            }
            if (oldStatus == TransactionStatus.Declined && status == TransactionStatus.Pending
                || oldStatus == TransactionStatus.Pending && status == TransactionStatus.Declined)
            {
                return true;
            }
            var account = DbContext.Accounts.Find(transaction.AccountId);
            _ = account ?? throw new InvalidOperationException($"Account with id={transaction.AccountId} does not exist");

            var amount = transaction.Amount;
            if (transaction.Direction == TransactionDirection.Outcome)
            {
                amount *= -1;
            }
            lock (_synchronizationObject)
            {
                var currentBalance = account.Balance;
                var state = (oldStatus, status);
                var newBalance = state switch
                {
                    (TransactionStatus.Declined, TransactionStatus.Completed) => currentBalance + amount,
                    (TransactionStatus.Pending, TransactionStatus.Completed) => currentBalance + amount,
                    (TransactionStatus.Completed, TransactionStatus.Declined) => currentBalance - amount,
                    (TransactionStatus.Completed, TransactionStatus.Pending) => currentBalance - amount,
                    _ => currentBalance
                };

                if (newBalance < 0)
                {
                    return false;
                }

                if (newBalance != currentBalance)
                {
                    account.Balance = newBalance;
                    DbContext.Accounts.Update(account);


                }
                return true;
            }


        }

        private void SaveChangesConcurrencyCheckEnabled()
        {
            const int maxRetryCount = 10;
            int retryCount = 0;
            while (retryCount < maxRetryCount)
            {
                try
                {
                    DbContext.SaveChanges();
                    return;
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    foreach (var entry in ex.Entries)
                    {
                        if (entry.Entity is Account)
                        {
                            var currentValues = entry.CurrentValues;
                            var databaseValues = entry.GetDatabaseValues();
                            foreach (var property in currentValues.Properties)
                            {
                                if (property.Name != nameof(Account.Balance))
                                {
                                    continue;
                                }
                                var currentBalance = (decimal)currentValues[property];
                                var databaseBalance = (decimal)databaseValues[property];
                                var originalBalance = (decimal)entry.OriginalValues[property];

                                decimal delta = currentBalance - originalBalance;
                                currentValues[property] = delta + databaseBalance;
                                break;
                            }
                            // Refresh original values to bypass next concurrency check
                            entry.OriginalValues.SetValues(databaseValues);
                        }

                    }
                }
                retryCount++;

            }

        }

    }
}
