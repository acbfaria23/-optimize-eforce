WITH cfg AS (
    SELECT /*+ MATERIALIZE */
        emp.cod_empresa,
        (SELECT TO_NUMBER(valor_long)
           FROM cd_parametros_globais
          WHERE seq_par = 316 AND sistema = 'SIGOD'
            AND cod_empresa = emp.cod_empresa) AS dias_vencimento,
        (SELECT TO_NUMBER(valor_long)
           FROM cd_parametros_globais
          WHERE seq_par = 221 AND sistema = 'SIGOD'
            AND cod_empresa = emp.cod_empresa) AS exibir_os_tec_prog,
        (SELECT valor_string
           FROM cd_parametros_globais
          WHERE seq_par = 317 AND sistema = 'SIGOD'
            AND cod_empresa = emp.cod_empresa) AS str_lista_excecao
      FROM (
            SELECT (SELECT TO_NUMBER(valor_long)
                      FROM cd_parametros_globais
                     WHERE seq_par = 1 AND cod_empresa = 0 AND sistema = 'COMUM') AS cod_empresa
              FROM dual
           ) emp
),
cfg_lista_excecao AS (
    SELECT /*+ MATERIALIZE */
           TO_NUMBER(TRIM(REGEXP_SUBSTR(c.str_lista_excecao, '[^,]+', 1, LEVEL))) AS codserv
      FROM cfg c
   CONNECT BY LEVEL <= REGEXP_COUNT(c.str_lista_excecao, ',') + 1
),
desp_servico AS (
    SELECT /*+ MATERIALIZE */ DISTINCT codserv
      FROM despachante_servico_disponivel
),
desp_cobertura AS (
    SELECT /*+ MATERIALIZE */ DISTINCT b.codreg, b.codpolo, b.codregiao
      FROM despachante_regiao b
      JOIN despachante_regiao_escala e
        ON e.sgl_despacho = b.sgl_despacho
       AND e.cod_regional = b.codreg
       AND e.cod_polo     = b.codpolo
       AND e.cod_regiao   = b.codregiao
),
vw_new AS (
    SELECT /*+ MATERIALIZE */ DISTINCT tipserv, numos
      FROM (
            SELECT a.tipserv, a.numos
              FROM mov_despacho a
             WHERE a.ind_despacho_realizado = 'N'
            UNION ALL
            SELECT a.tipserv, a.numos
              FROM vw_despacho_os_equipe a
             WHERE a.cod_sit < 3
                OR (a.cod_sit = 9 AND a.inddesp_ret = 1)
                OR (a.cod_sit = 4 and TRUNC(a.DH_DESALOCACAO) = TRUNC(SYSDATE) )
           
      )
),
vw_new_lote AS (
    SELECT /*+ MATERIALIZE */ DISTINCT numlote
      FROM mov_despacho_lote_item
     WHERE numlote_reenvio IS NULL
),
mv_filtrado AS (
    SELECT
           mv.tipserv, mv.numos, mv.codserv, mv.codreg, mv.codpolo, mv.codregiao,
           mv.dsc_numero_os, mv.data_combinada, mv.data_sol, mv.data_venc,
           mv.idt_localizacao, mv.vlr_coordenada_latitude, mv.vlr_coordenada_longitude,
           mv.ind_cliente_essencial, mv.ind_cliente_hospital, mv.qtd_cliente_atingido,
           mv.qtd_cliente_atingido_vip, mv.ind_os_regulada, mv.indsrv_pdd,
           mv.idt_tipo_ocorrencia, mv.num_cdc
      FROM mov_despacho mv
     CROSS JOIN cfg
     WHERE mv.tipserv IN ('C','T')
       AND mv.prioridade IS NOT NULL
       AND mv.ind_despacho_cancelado = 'N'
       AND mv.vlr_coordenada_latitude  IS NOT NULL
       AND mv.vlr_coordenada_longitude IS NOT NULL
       AND mv.vlr_coordenada_latitude  <> 0
       AND mv.vlr_coordenada_longitude <> 0
       AND mv.vlr_coordenada_latitude  BETWEEN -34 AND 6
       AND mv.vlr_coordenada_longitude BETWEEN -74 AND -28
       AND mv.data_sol >= CASE
                             WHEN mv.tipserv = 'T' THEN TRUNC(SYSDATE) - 1
                             WHEN mv.tipserv = 'C' THEN TRUNC(SYSDATE) - 30
                         END
       AND EXISTS (SELECT 1 FROM desp_servico d WHERE d.codserv = mv.codserv)
       AND EXISTS (
             SELECT 1 FROM vw_new v
              WHERE v.tipserv = mv.tipserv AND v.numos = mv.numos
       )
       AND NOT EXISTS (
             SELECT 1 FROM bloqueio_despacho bd
              WHERE bd.ind_tipo_servico = mv.tipserv
                AND bd.num_os = mv.numos
                AND bd.dth_desbloqueio IS NULL
       )
       AND (
              EXISTS (SELECT 1 FROM desp_cobertura d
                       WHERE d.codregiao > 0 AND d.codpolo > 0
                         AND d.codreg = mv.codreg AND d.codpolo = mv.codpolo
                         AND d.codregiao = mv.codregiao)
           OR EXISTS (SELECT 1 FROM desp_cobertura d
                       WHERE d.codregiao = 0 AND d.codpolo > 0
                         AND d.codreg = mv.codreg AND d.codpolo = mv.codpolo)
           OR EXISTS (SELECT 1 FROM desp_cobertura d
                       WHERE d.codregiao = 0 AND d.codpolo = 0
                         AND d.codreg = mv.codreg)
       )
       AND NOT (
             mv.tipserv = 'T'
         AND mv.idt_tipo_ocorrencia = 'P'
         AND cfg.exibir_os_tec_prog = 0
       )
       AND (
              (mv.tipserv = 'C'
               AND EXISTS (SELECT 1 FROM os_digitacao od
                            WHERE od.numos = mv.numos
                              AND od.pto_controle < 3))
           OR (mv.tipserv = 'T'
               AND EXISTS (SELECT 1 FROM ocorrencias_pendentes op
                            WHERE op.ano_ocorrencia = TRUNC(mv.numos / 10000000000)
                              AND op.num_ocorrencia = MOD(mv.numos, 10000000000)))
       )
      -- AND mv.codserv not in (569,3)
       AND mv.data_venc <= TRUNC(sysdate +7)
       and mv.data_sol < (TRUNC(SYSDATE) + INTERVAL '5' HOUR)
),
lct_filtrado AS (
    SELECT
           lct.numlote, lct.codsvc_lot, lct.codreg, lct.codpolo, lct.codregiao,
           lct.data_sol, lct.data_venc
      FROM mov_despacho_lote lct
     CROSS JOIN cfg
     WHERE lct.cod_situacao <= 3
       AND lct.prioridade IS NOT NULL
       AND lct.data_sol >= TRUNC(SYSDATE) - 30
       AND EXISTS (SELECT 1 FROM desp_servico d WHERE d.codserv = lct.codsvc_lot)
       AND NOT EXISTS (
             SELECT 1 FROM bloqueio_despacho bd
              WHERE bd.num_lote = lct.numlote
                AND bd.dth_desbloqueio IS NULL
       )
       AND (
              EXISTS (SELECT 1 FROM desp_cobertura d
                       WHERE d.codregiao > 0 AND d.codpolo > 0
                         AND d.codreg = lct.codreg AND d.codpolo = lct.codpolo
                         AND d.codregiao = lct.codregiao)
           OR EXISTS (SELECT 1 FROM desp_cobertura d
                       WHERE d.codregiao = 0 AND d.codpolo > 0
                         AND d.codreg = lct.codreg AND d.codpolo = lct.codpolo)
           OR EXISTS (SELECT 1 FROM desp_cobertura d
                       WHERE d.codregiao = 0 AND d.codpolo = 0
                         AND d.codreg = lct.codreg)
       )
       AND EXISTS (SELECT 1 FROM vw_new_lote vl WHERE vl.numlote = lct.numlote)
       and (lct.data_sol < (TRUNC(SYSDATE) + INTERVAL '5' HOUR)
       OR (lct.cod_situacao = 4 and TRUNC(SYSDATE) = (select trunc(min(lct2.datatertrab)) from mov_despacho_lote_item lct2 where lct2.numlote = lct.numlote )))
),
tme_mae_filha AS (
    SELECT
        m.cod_atividade                    AS codserv_mae,
        m.vlr_meta_tme + f.vlr_meta_tme   AS vlr_meta_tme_somado
    FROM meta_tme_atividade_mip m
    JOIN meta_tme_atividade_mip f
        ON f.sgl_sistema_origem_atividade = 'SIATE'
       AND f.cod_operacao                 = 1
       AND f.cod_atividade = CASE m.cod_atividade
                                WHEN  '28'  THEN '1'
                                WHEN  '79'  THEN '18'
                                WHEN '709'  THEN '148'
                                WHEN '710'  THEN '149'
                                WHEN '711'  THEN '712'
                                WHEN '738'  THEN '740'
                                WHEN '739'  THEN '741'
                                WHEN '784'  THEN '782'
                                WHEN '785'  THEN '783'
                                WHEN '798'  THEN '801'
                             END
    WHERE m.sgl_sistema_origem_atividade = 'SIATE'
      AND m.cod_operacao                 = 1
      AND m.cod_atividade IN ('28', '79', '709', '710', '711', '738', '739', '784', '785', '798')
),
resultado AS (
SELECT DISTINCT
    cfg.cod_empresa AS COD_EMPRESA,
    re.codregiao AS REGIAO_DESPACHO,
    cfg.cod_empresa || '_' || re.codregiao AS id_unico_regiao_despacho,

    TO_CHAR(mv.numos) AS NUMERO_OS,

    mv.tipserv AS TIPO_OS,

    TO_CHAR(mv.numos) || mv.tipserv AS id_unico_os,

    mv.codserv AS CODSERV,
    osd.tipo_corte AS TIPO_CORTE,
    tc.dsc_corte   AS descricao_tipo_corte,

    TO_NUMBER(NULL) AS NUM_LOTE,
    TO_NUMBER(NULL) AS NUMCDC_LOTE,

    CASE WHEN mv.tipserv = 'T' THEN mv.data_sol else CASE WHEN mv.data_combinada > mv.data_sol THEN mv.data_combinada ELSE mv.data_sol END END AS DATA_SOLICITACAO,
    CASE WHEN mv.tipserv = 'T' THEN  mv.data_sol ELSE mv.data_venc END AS DATA_VENCIMENTO,
    raeo.dat_agendamento AS DATA_AGENDAMENTO,

    mv.idt_localizacao AS LOCALIZACAO,
    mv.vlr_coordenada_latitude  AS LATITUDE_OS,
    mv.vlr_coordenada_longitude AS LONGITUDE_OS,

    CASE
        WHEN mv.tipserv = 'T' THEN 30
        ELSE NVL(tmf.vlr_meta_tme_somado, mtm.vlr_meta_tme)
    END + 5 AS META_TME,

    CASE WHEN sgd.motivo = 1 THEN 'S' ELSE 'N' END AS RISCO_VIDA,
    CASE WHEN mv.ind_cliente_essencial = 'S' THEN 'S' ELSE 'N' END AS CLIENTE_ESSENCIAL,

    CASE WHEN mv.tipserv = 'T' THEN NVL(mv.qtd_cliente_atingido, 1) ELSE 1 END AS PESSOAS_ATINGIDAS,

    CASE
        WHEN mv.tipserv = 'C'
        THEN CASE
                 WHEN EXISTS (
                         SELECT 1
                           FROM prazo_atendimento_servico pas
                           JOIN prazo_atendimento pa
                             ON pa.codtip_atd = pas.codtip_atd
                           JOIN instalacao ins
                             ON ins.numcdc_itc = mv.num_cdc
                           JOIN tensao_fornecimento tf
                             ON tf.cod_tensao = ins.indten_fnm_itc
                          WHERE pas.codsvc             = mv.codserv
                            AND pa.indsit              = 'A'
                            AND pa.ind_anexo_regulacao  = 4
                            AND (pa.idttip_loc_srv IS NULL OR pa.idttip_loc_srv = mv.idt_localizacao)
                            AND (pa.idtgrp_ltr    IS NULL OR pa.idtgrp_ltr    = osd.codgrupo_leit)
                            AND (pa.vlr_tensao_fornecimento_inicl IS NULL
                                 OR (  tf.vlr_tensao >= pa.vlr_tensao_fornecimento_inicl * 1000
                                    AND tf.vlr_tensao <= pa.vlr_tensao_fornecimento_final * 1000))
                      )
                 THEN 'S' ELSE 'N'
             END
        WHEN mv.tipserv = 'T'
        THEN CASE WHEN mv.ind_os_regulada = 'S' THEN 'S' ELSE 'N' END
        ELSE 'N'
    END AS OS_REGULADA,

    CASE
        WHEN mv.tipserv = 'C' THEN ioc.vlr_total_indenizacao
        WHEN mv.tipserv = 'T' THEN io.vlrtot_idz
    END AS COMPENSACAO,

    CASE
        WHEN mv.tipserv = 'C' THEN ioc.vlr_total_indenizacao
        WHEN mv.tipserv = 'T' THEN io.vlrtot_idz
    END AS COMPENSACAO_POR_HORA,

    CAST(NULL AS VARCHAR2(1)) AS REICIDENCIA,

    CASE
        WHEN mv.indsrv_pdd = 'S'
          OR sgd.motivo = 1
          OR mv.ind_cliente_hospital = 'S'
          OR mv.ind_cliente_essencial = 'S'
        THEN 'Urgente'
        WHEN mv.indsrv_pdd IN ('L','J')
          OR NVL(mv.qtd_cliente_atingido_vip, 0) > 0
        THEN 'Alta'
        ELSE 'Normal'
    END AS PRIORIDADE

FROM mv_filtrado mv
CROSS JOIN cfg
LEFT JOIN regiao_despacho re
       ON re.codreg = mv.codreg AND re.codpolo = mv.codpolo AND re.codregiao = mv.codregiao
LEFT JOIN responsavel_acpto_execc_os raeo
       ON raeo.num_os = mv.numos AND raeo.ind_tipo_servico = mv.tipserv
LEFT JOIN sgd_sigod sgd
       ON (sgd.ano_ocorrencia * 10000000000 + sgd.num_ocorrencia) = mv.numos
LEFT JOIN meta_tme_atividade_mip mtm
       ON mtm.cod_atividade = mv.codserv
      AND mtm.sgl_sistema_origem_atividade = 'SIATE'
      AND mtm.cod_operacao = 1
LEFT JOIN tme_mae_filha tmf
       ON tmf.codserv_mae = mv.codserv
LEFT JOIN indenizacao_os_comercial ioc ON ioc.num_os = mv.numos
LEFT JOIN indenizacao_ocorrencia io
       ON io.numoco = mv.numos AND io.numseq_tem = 0
LEFT JOIN os_digitacao osd ON osd.numos = mv.numos
LEFT JOIN tipo_corte tc    ON tc.tipo_corte = osd.tipo_corte
LEFT JOIN prazo_atendimento_servico pas
       ON pas.codsvc    = mv.codserv
LEFT JOIN prazo_atendimento pa
       ON pa.codtip_atd = pas.codtip_atd

UNION ALL

SELECT DISTINCT
    cfg.cod_empresa AS COD_EMPRESA,
    re.codregiao AS REGIAO_DESPACHO,
    cfg.cod_empresa || '_' || re.codregiao AS id_unico_regiao_despacho,

    TO_CHAR(lct.numlote) || '_' || TO_CHAR(mdli.numcdc) AS NUMERO_OS,
    'X' AS TIPO_OS,
    TO_CHAR(lct.numlote) || '_' || TO_CHAR(mdli.numcdc) || 'X' AS id_unico_os,

    lct.codsvc_lot AS CODSERV,
    mdli.tipo_corte AS TIPO_CORTE,
    tc.dsc_corte    AS descricao_tipo_corte,

    lct.numlote AS NUM_LOTE,
    mdli.numcdc AS NUMCDC_LOTE,

    lct.data_sol  AS DATA_SOLICITACAO,
    lct.data_venc AS DATA_VENCIMENTO,
    NULL AS DATA_AGENDAMENTO,

    NULL AS LOCALIZACAO,
    mdli.vlr_coordenada_latitude  AS LATITUDE_OS,
    mdli.vlr_coordenada_longitude AS LONGITUDE_OS,

    CASE
        WHEN lct.codsvc_lot IN (5, 6, 198, 383)
        THEN NVL(tmf.vlr_meta_tme_somado, mtm.vlr_meta_tme)
        ELSE NVL(tmf.vlr_meta_tme_somado, mtm.vlr_meta_tme) + 5
    END AS META_TME,

    'N' AS RISCO_VIDA,
    'N' AS CLIENTE_ESSENCIAL,
    1 AS PESSOAS_ATINGIDAS,
    'N' AS OS_REGULADA,
    0 AS COMPENSACAO,
    0 AS COMPENSACAO_POR_HORA,
    CAST(NULL AS VARCHAR2(1)) AS REICIDENCIA,
    'Normal' AS PRIORIDADE

FROM lct_filtrado lct
CROSS JOIN cfg
INNER JOIN mov_despacho_lote_item mdli
        ON mdli.numlote = lct.numlote
       AND mdli.numlote_reenvio IS NULL
       AND mdli.vlr_coordenada_latitude  IS NOT NULL
       AND mdli.vlr_coordenada_longitude IS NOT NULL
       AND mdli.vlr_coordenada_latitude  <> 0
       AND mdli.vlr_coordenada_longitude <> 0
       AND mdli.vlr_coordenada_latitude  BETWEEN -34 AND 6
       AND mdli.vlr_coordenada_longitude BETWEEN -74 AND -28
LEFT JOIN regiao_despacho re
       ON re.codreg = lct.codreg AND re.codpolo = lct.codpolo AND re.codregiao = lct.codregiao
LEFT JOIN meta_tme_atividade_mip mtm
       ON mtm.cod_atividade = lct.codsvc_lot
      AND mtm.sgl_sistema_origem_atividade = 'SIATE'
      AND mtm.cod_operacao = 1
LEFT JOIN tme_mae_filha tmf
       ON tmf.codserv_mae = lct.codsvc_lot
LEFT JOIN tipo_corte tc ON tc.tipo_corte = mdli.tipo_corte
),
resultado_validado AS (
    SELECT r.*,
           COUNT(*) OVER (
               PARTITION BY r.id_unico_os
           ) AS qtd_id_unico
      FROM resultado r
)
SELECT *
  FROM resultado_validado
 WHERE qtd_id_unico = 1
 AND (TIPO_OS <> 'C' OR OS_REGULADA = 'S')