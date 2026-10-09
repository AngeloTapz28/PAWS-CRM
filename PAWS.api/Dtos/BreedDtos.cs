using System.ComponentModel.DataAnnotations;

namespace PAWS.api.Dtos;

public class BreedDto
{
    public int BreedId { get; set; }
    public int PetCategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string BreedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public class CreateBreedRequestDto
{
    [Required]
    public int PetCategoryId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string BreedName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}

public class UpdateBreedRequestDto
{
    [Required]
    public int PetCategoryId { get; set; }

    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string BreedName { get; set; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; set; }
}