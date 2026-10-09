namespace PAWS.winui.Models;

// A payable application including the pet's adoption fee.
// The Application dropdown shows DisplayText.
public class PayableApplicationFeeItem
{
    public int AdoptionApplicationId { get; set; }
    public string ApplicationNumber { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public string ApplicantName { get; set; } = string.Empty;
    public decimal? AdoptionFee { get; set; }

    public string DisplayText => $"{ApplicationNumber} \u2014 {PetName} ({ApplicantName})";
}