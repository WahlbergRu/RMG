const fs = require('fs');
const path = require('path');

// --- НАСТРОЙКИ ---
const OUTPUT_FILE = 'csharp_folder_code.txt';
const INCLUDE_EXTENSIONS = ['.cs']; // Можно добавить '.csproj'
const IGNORE_DIRS = ['bin', 'obj', 'packages', '.git', '.vs', '.idea', '.vscode', 'TestResults'];

// --- ЛОГИКА ---

async function getFiles(dir) {
  const dirents = await fs.promises.readdir(dir, { withFileTypes: true });
  const files = await Promise.all(dirents.map((dirent) => {
    const res = path.resolve(dir, dirent.name);

    if (dirent.isDirectory()) {
      if (IGNORE_DIRS.includes(dirent.name)) return [];
      return getFiles(res);
    } else {
      const ext = path.extname(dirent.name).toLowerCase();
      if (INCLUDE_EXTENSIONS.includes(ext)) return res;
      return [];
    }
  }));
  return Array.prototype.concat(...files);
}

async function bundleCodebase() {
  try {
    // === ВОТ ТУТ МАГИЯ: читаем аргумент командной строки ===
    // process.argv[2] — это то, что идет после "node bundle.js"
    const targetFolder = process.argv[2] || '.'; 
    const rootDir = path.resolve(process.cwd(), targetFolder);

    // Проверка, существует ли папка
    if (!fs.existsSync(rootDir)) {
        console.error(`❌ Ошибка: Папка "${targetFolder}" не найдена!`);
        return;
    }

    console.log(`🔍 Сканирование папки: ${rootDir}`);
    
    const allFiles = await getFiles(rootDir);
    
    if (allFiles.length === 0) {
        console.log(`⚠️ В папке "${targetFolder}" не найдено .cs файлов.`);
        return;
    }

    console.log(`📄 Найдено файлов: ${allFiles.length}`);
    
    const stream = fs.createWriteStream(OUTPUT_FILE, { encoding: 'utf8' });

    for (const filePath of allFiles) {
      // Путь в файле будет относительно папки запуска, чтобы было понятно
      const relativePath = path.relative(process.cwd(), filePath);
      
      try {
        const content = await fs.promises.readFile(filePath, 'utf8');
        stream.write('// ' + '='.repeat(60) + '\n');
        stream.write(`// FILE: ${relativePath}\n`);
        stream.write('// ' + '='.repeat(60) + '\n');
        stream.write(content + '\n\n');
      } catch (err) {
        console.error(`❌ Ошибка чтения ${relativePath}:`, err.message);
      }
    }

    stream.end();
    console.log(`✅ Готово! Файл создан: ${OUTPUT_FILE}`);
    
  } catch (err) {
    console.error('❌ Ошибка:', err.message);
  }
}

bundleCodebase();