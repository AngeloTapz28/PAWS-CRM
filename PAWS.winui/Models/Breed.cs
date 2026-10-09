namespace PAWS.winui.Models;

public class BreedItem
{
    public int BreedId { get; set; }
    public int PetCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string BreedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class CreateBreedRequest
{
    public int PetCategoryId { get; set; }
    public string BreedName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateBreedRequest
{
    public int PetCategoryId { get; set; }
    public string BreedName { get; set; } = string.Empty;
    public string? Description { get; set; }
}