using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

namespace Service.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountingServiceController : ControllerBase
    {

        public AccountingServiceController(AccountingService service)
        {
            Service = service;
        }

        public AccountingService Service { get; }


        [HttpGet]
        [Route("/CreateAccount")]
        public IActionResult CreateAccount(string name, string currency, decimal initialAmount)
        {
            try
            {
                var account = Service.CreateNewAccount(name, currency, initialAmount); 
                return Ok($"id={account.Id}, name={account.Name} currency={account.Currency} Balance={initialAmount}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(500);
            }
        }


        [HttpGet]
        [Route("/AddFunds")]
        public IActionResult AddFunds(int accountId, decimal amount, string currency = null)
        {
            try
            {
                var transaction = Service.CreateIncomingTransaction(accountId, amount, currency);
                return Ok($"id={transaction.Id}, status={transaction.Status} amount ={transaction.Amount}");
            }
            catch (InvalidOperationException ex)
            {
               return BadRequest(ex.Message);
            }
            catch(Exception)
            {
               return StatusCode(500);
            }
            
        }

        [HttpGet]
        [Route("/WithdrawFunds")]
        public IActionResult WithdrawFunds(int accountId, decimal amount, string currency = null)
        {
            try
            {
                var transaction = Service.CreateOutcomingTransaction(accountId, amount, currency);
                return Ok($"id={transaction.Id}, status={transaction.Status} amount={transaction.Amount}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(500);
            }

        }

        [HttpGet]
        [Route("/UpdateTransactionStatus")]
        public IActionResult UpdateTransactionStatus(int transactionId, TransactionStatus status)
        {
            try
            {
                var transaction = Service.UpdateTransactionStatus(transactionId, status);
                return Ok($"id={transaction.Id}, status={transaction.Status} amount={transaction.Amount}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(500);
            }

        }

        [HttpGet]
        [Route("/TransferFunds")]
        public IActionResult TransferFunds(int senderId, int receiverId, decimal amount)
        {
            try
            {
                var (outcoming, incoming) = Service.CreateTransferTransactions(senderId, receiverId, amount);
                return Ok($"id={outcoming.Id}, status={outcoming.Status} amount={outcoming.Amount}, currency={outcoming.Currency}" +
                          $"id={incoming.Id}, status={incoming.Status} amount={incoming.Amount}, currency={incoming.Currency}");
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception)
            {
                return StatusCode(500);
            }

        }
    }
}
