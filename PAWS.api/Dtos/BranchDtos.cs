namespace PAWS.api.Dtos;

public class BranchDto
{
    public int BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PetCount { get; set; }
}

public class CreateBranchRequestDto
{
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
}

public class UpdateBranchRequestDto
{
    public string BranchName { get; set; } = string.Empty;
    public string? Address { get; set; }
}