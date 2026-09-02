namespace ConferenceApp.Helpers
{
    public static class FileHelper
    {
        /// <summary>
        /// Ако oldFilePath != null, изтрива стария файл (ако съществува).
        /// Ако file != null, записва го в зададената папка и връща относителния път.
        /// Ако file == null и oldFilePath != null, връща null (файлът е премахнат).
        /// Ако няма файл за запис, връща oldFilePath.
        /// </summary>
        /// 

        public static async Task<string> SaveOrRemoveFileAsync(IFormFile? file, string? oldFilePath = null, string? folder = null)
        {
            // Изтриване на стария файл, ако има такъв
            if (!string.IsNullOrEmpty(oldFilePath))
            {
                var oldPhysicalPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldFilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldPhysicalPath))
                {
                    System.IO.File.Delete(oldPhysicalPath);
                }
            }

            // Ако няма нов файл за запис, връщаме null (файл премахнат)
            if (file == null || file.Length == 0)
            {
                return null;
            }

            // Записваме новия файл
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            // Ако folder е null или празен, използваме само "uploads"
            var uploadFolder = string.IsNullOrWhiteSpace(folder) ? "uploads" : Path.Combine("uploads", folder);

            var uploadPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", uploadFolder);

            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            var filePath = Path.Combine(uploadPath, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Връщаме относителен път с / (за web), като заменяме \ с /
            return "/" + uploadFolder.Replace('\\', '/') + "/" + fileName;
        }
    }
}
