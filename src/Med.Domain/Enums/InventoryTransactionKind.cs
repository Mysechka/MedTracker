namespace Med.Domain.Enums;

public enum InventoryTransactionKind
{
    /// <summary>Списание при Taken.</summary>
    Debit = 0,
    /// <summary>Компенсация при undo Taken.</summary>
    Credit = 1,
    /// <summary>Ручное пополнение остатка.</summary>
    Restock = 2,
}
