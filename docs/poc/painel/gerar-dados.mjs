// Uso: node docs/poc/painel/gerar-dados.mjs
// Junta indice.json e os JSON de cada dia em painel/dados.js, para o painel abrir via file://.
// Rode de novo sempre que um dia for incluído ou um JSON for regerado.
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";

const painel = path.dirname(fileURLToPath(import.meta.url));
const raiz = path.resolve(painel, "..");

export const gerarDados = () => {
  const ler = (arquivo) => JSON.parse(fs.readFileSync(path.join(raiz, arquivo), "utf8"));
  const indice = ler("indice.json");
  const arquivos = {};
  for (const dia of indice.dias) {
    for (const arquivo of [dia.resultado, dia.simulacao].filter(Boolean)) arquivos[arquivo] = ler(arquivo);
  }
  if (fs.existsSync(path.join(raiz, "historico-simulacoes.json"))) arquivos["historico-simulacoes.json"] = ler("historico-simulacoes.json");
  const saida = path.join(painel, "dados.js");
  fs.writeFileSync(saida, `window.POC_DADOS = ${JSON.stringify({ "indice.json": indice, ...arquivos })};\n`, "utf8");
  return { saida, dias: indice.dias.length };
};

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const { saida, dias } = gerarDados();
  console.log(`Gravado ${path.relative(process.cwd(), saida)} com ${dias} dia(s).`);
}
