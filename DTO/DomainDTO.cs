using System.ComponentModel.DataAnnotations;

namespace MyBGList.DTO
{
    public class DomainDTO : IValidatableObject
    {
        [Required]
        public int Id { get; set; }

        // not null, not empty, contains only uppercase and lowercase letters
        // invalid => "Value must contain only letter (no space, digits, or other characters)"

        //[Required(ErrorMessage = "Value must not null or empty")]
        [RegularExpression(@"^[a-zA-Z]+$",
            ErrorMessage = "Value must contain only letter (no space, digits, or other characters)")]

        public string? Name { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            return (Id != 3 && Name != "Wargames")
                ? new[] { new ValidationResult(
                    "Id and/or Name values must match an allowed Domain.")}
                : new ValidationResult[0];
        }
    }
}
