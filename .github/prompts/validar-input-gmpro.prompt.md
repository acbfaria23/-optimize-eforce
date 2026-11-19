---
description: "Valida um JSON de input GMPro (OptimizeToursRequest) e lista todas as inconsistências com causa raiz no código"
name: "validar-input-gmpro"
argument-hint: "Caminho do JSON de input e, opcionalmente, a mensagem de erro da GMPro"
agent: "Bug Hunter GMPro"
---
Analise o arquivo de input GMPro informado (ou o anexado/aberto no editor) e produza o relatório completo de inconsistências no formato do agente, incluindo a causa raiz no código e o que não foi verificado.

Se houver mensagem de erro da GMPro, explique-a e procure também os demais erros que a Google não exibiu.
