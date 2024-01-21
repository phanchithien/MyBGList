using Microsoft.Extensions.Primitives;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace MyBGList.Attributes
{
    public class LettersOnlyValidatorAttribute : ValidationAttribute
    {
        public bool UseRegex { get; set; } = false;
        public LettersOnlyValidatorAttribute() : 
            base ("Value must contain only letter (no space, digits, or other characters)") { }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            var strValue = value as string;

            if (!string.IsNullOrEmpty(strValue) 
                && (UseRegex && (Regex.IsMatch(strValue, "^[A-Za-z]+$"))))
            {
                return ValidationResult.Success;
            }

            return new ValidationResult(ErrorMessage);
        }
    }
}
