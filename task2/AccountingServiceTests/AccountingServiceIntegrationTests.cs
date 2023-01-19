using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Service;
using System;
using System.Linq;
using System.Threading;
using Xunit;

namespace AccountingServiceTests
{
    public class AccountingServiceIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        public AccountingServiceIntegrationTests(WebApplicationFactory<Program> factory)
        {
            Factory = factory;
        }

        public WebApplicationFactory<Program> Factory { get; }

        [Fact]
        public void TestCreateCompletedIncomingTransactions()
        {
            using var scope = GetServices(out var dbContext, out var accountingService);

            var account = dbContext.Accounts.First();
            var balanceBeforeTransaction = account.Balance;
            var amount = 50.0m;
            var transaction = accountingService.CreateIncomingTransaction(account.Id, amount, account.Currency);

            var transactionFromDb = dbContext.Transactions.Find(transaction.Id);
            Assert.NotNull(transactionFromDb);
            Assert.Equal(TransactionStatus.Completed, transaction.Status);
            Assert.Equal(transaction.Currency, account.Currency);
            Assert.Equal(balanceBeforeTransaction + amount, account.Balance);


            var differentCurrency = dbContext.Accounts.Where(a => a.Currency != account.Currency)
                                                      .Select(a => a.Currency).First();
            Assert.Throws<InvalidOperationException>(() => accountingService.CreateIncomingTransaction(account.Id, amount, differentCurrency));



            balanceBeforeTransaction = account.Balance;
            amount = 50.0m;
            transaction = accountingService.CreateIncomingTransaction(account.Id, amount);

            transactionFromDb = dbContext.Transactions.Find(transaction.Id);
            Assert.NotNull(transactionFromDb);
            Assert.Equal(TransactionStatus.Completed, transaction.Status);
            Assert.Equal(transaction.Currency, account.Currency);
            Assert.Equal(balanceBeforeTransaction + amount, account.Balance);
        }

        [Theory]
        [InlineData(TransactionStatus.Pending)]
        [InlineData(TransactionStatus.Declined)]
        public void TestCreateIncompletedTransactions(TransactionStatus status)
        {
            using var scope = GetServices(out var dbContext, out var accountingService);

            var account = dbContext.Accounts.First();
            var balanceBeforeTransaction = account.Balance;
            var amount = 50.0m;
            var transaction = accountingService.CreateIncomingTransaction(account.Id, amount, account.Currency, status);

            var transactionFromDb = dbContext.Transactions.Find(transaction.Id);
            Assert.NotNull(transactionFromDb);
            Assert.Equal(status, transaction.Status);
            Assert.Equal(transaction.Currency, account.Currency);
            Assert.Equal(balanceBeforeTransaction, account.Balance);

            transaction = accountingService.CreateOutcomingTransaction(account.Id, amount, account.Currency, status);

            transactionFromDb = dbContext.Transactions.Find(transaction.Id);
            Assert.NotNull(transactionFromDb);
            Assert.Equal(status, transaction.Status);
            Assert.Equal(transaction.Currency, account.Currency);
            Assert.Equal(balanceBeforeTransaction, account.Balance);

        }


        [Fact]
        public void TestCreatCompletedOutcomingTransactions()
        {
            using var scope = GetServices(out var dbContext, out var accountingService);

            var account = dbContext.Accounts.First();
            var balanceBeforeTransaction = account.Balance;
            var amount = balanceBeforeTransaction * 0.1m;
            var transaction = accountingService.CreateOutcomingTransaction(account.Id, amount, account.Currency);

            var transactionFromDb = dbContext.Transactions.Find(transaction.Id);
            Assert.NotNull(transactionFromDb);
            Assert.Equal(TransactionStatus.Completed, transaction.Status);
            Assert.Equal(transaction.Currency, account.Currency);
            Assert.Equal(balanceBeforeTransaction - amount, account.Balance);

            balanceBeforeTransaction = account.Balance;
            amount = balanceBeforeTransaction + 20m;
            transaction = accountingService.CreateOutcomingTransaction(account.Id, amount, account.Currency);

            transactionFromDb = dbContext.Transactions.Find(transaction.Id);
            Assert.NotNull(transactionFromDb);
            Assert.Equal(TransactionStatus.Declined, transaction.Status);
            Assert.Equal(transaction.Currency, account.Currency);
            Assert.Equal(balanceBeforeTransaction, account.Balance);
        }

        

        [Fact]
        public void TestConcurrentBalanceUpdates()
        {
            using var scope = GetServices(out var dbContext, out var accountingService);

            var account = dbContext.Accounts.First();
            var balanceBeforeTransactions = account.Balance;
            ManualResetEvent synchronizationEvent = new ManualResetEvent(false);
            ManualResetEvent thread1 = new ManualResetEvent(false);
            ManualResetEvent thread2 = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                synchronizationEvent.WaitOne();
                for (int i = 0; i < 5; i++)
                {
                    accountingService.CreateIncomingTransaction(account.Id, 5m);
                }
                thread1.Set();
            });
            using var scope2 = GetServices(out var dbContext2, out var accountingService2);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                synchronizationEvent.Set();
                for (int i = 0; i < 5; i++)
                {
                    accountingService2.CreateIncomingTransaction(account.Id, 3m);
                }
                thread2.Set();
            });

            thread1.WaitOne();
            thread2.WaitOne();

            using (GetServices(out var cleanContext, out _))
            {
                var accountFromDb = cleanContext.Accounts.Find(account.Id)!;
                Assert.Equal(balanceBeforeTransactions + 40, accountFromDb.Balance);
                balanceBeforeTransactions = accountFromDb.Balance;
            }

            synchronizationEvent.Reset();
            thread1.Reset();
            thread2.Reset();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                synchronizationEvent.WaitOne();
                for (int i = 0; i < 5; i++)
                {
                    accountingService.CreateOutcomingTransaction(account.Id, 5m);
                }
                thread1.Set();
            });
            ThreadPool.QueueUserWorkItem(_ =>
            {
                synchronizationEvent.Set();
                for (int i = 0; i < 5; i++)
                {
                    accountingService2.CreateOutcomingTransaction(account.Id, 3m);
                }
                thread2.Set();
            });
            thread1.WaitOne();
            thread2.WaitOne();
            using (GetServices(out var cleanContext, out _))
            {
                var accountFromDb = cleanContext.Accounts.Find(account.Id)!;
                Assert.Equal(balanceBeforeTransactions - 40, accountFromDb.Balance);
                balanceBeforeTransactions = accountFromDb.Balance;
            }
   
        }

        [Fact]
        public void TestMultiThreadedBalanceUpdates()
        {
            using var scope = GetServices(out var dbContext, out var accountingService);

            var account = dbContext.Accounts.First();
            var balanceBeforeTransaction = account.Balance;
            ManualResetEvent synchronizationEvent = new ManualResetEvent(false);
            ManualResetEvent thread1 = new ManualResetEvent(false);
            ManualResetEvent thread2 = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                synchronizationEvent.WaitOne();
                accountingService.CreateIncomingTransaction(account.Id, 5m);
                accountingService.CreateIncomingTransaction(account.Id, 5m);
                thread1.Set();
            });

            ThreadPool.QueueUserWorkItem(_ =>
            {
                synchronizationEvent.Set();
                accountingService.CreateIncomingTransaction(account.Id, 3m);
                accountingService.CreateIncomingTransaction(account.Id, 3m);
                thread2.Set();
            });

            thread1.WaitOne();
            thread2.WaitOne();
            Assert.Equal(balanceBeforeTransaction + 16, account.Balance);

        }


        private IServiceScope GetServices(out BankingContext dbContext, out AccountingService accountingService)
        {
            var scope = Factory.Services.CreateScope();
            dbContext = scope.ServiceProvider.GetService<BankingContext>()!;
            accountingService = scope.ServiceProvider.GetService<AccountingService>()!;
            return scope;
        }


    }
}