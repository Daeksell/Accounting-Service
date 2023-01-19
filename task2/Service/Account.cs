using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Service
{
    [Table("accounts")]
    public class Account
    {
        [Column("id")]
        public int Id { get; set; }
    
        [Column("name")]
        public string Name { get; set; }
    
        [Column("balance")]
        [ConcurrencyCheck]
        public decimal Balance { get; set; }
    
        [Column("currency")]
        public string Currency { get; set; }
    }
}